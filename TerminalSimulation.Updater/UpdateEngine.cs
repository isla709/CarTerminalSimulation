using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace TerminalSimulation.Updater;

public sealed class UpdateEngine
{
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);

    public async Task<UpdateResult> RunAsync(
        string planPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
        => await RunCoreAsync(planPath, progress, cancellationToken, null);

    internal Task<UpdateResult> RunLocalAsync(string planPath, string package, IProgress<UpdateProgress>? progress, CancellationToken token)
        => RunCoreAsync(planPath, progress, token, Path.GetFullPath(package));

    private async Task<UpdateResult> RunCoreAsync(string planPath, IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken, string? localPackage)
    {
        var workingDirectory = Path.Combine(Path.GetDirectoryName(planPath)!, "work");
        var packagePath = Path.Combine(workingDirectory, "package.bin");
        var stagingDirectory = Path.Combine(workingDirectory, "staging");
        var backupDirectory = Path.Combine(workingDirectory, "backup");
        FileTransaction? transaction = null;
        UpdatePlan? activePlan = null;
        KernelState? state = null;
        FileStream? installationLock = null;

        try
        {
            Directory.CreateDirectory(workingDirectory);
            var plan = await LoadAndValidatePlanAsync(planPath, cancellationToken);
            activePlan = plan;
            Directory.CreateDirectory(UpdateKernel.Root(plan.TargetDirectory));
            installationLock = new FileStream(Path.Combine(UpdateKernel.Root(plan.TargetDirectory), "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            state = UpdateKernel.ReadState(plan.TargetDirectory);
            UpdateKernel.ValidateState(plan, state);
            progress?.Report(new UpdateProgress("正在等待主程序安全退出…", null, plan.Version));
            await WaitForParentExitAsync(plan.ParentProcessId, cancellationToken);
            if (state.PendingTransaction.Length > 0)
            {
                FileTransaction.Recover(plan.TargetDirectory, state.PendingTransaction);
                state.PendingTransaction = "";
                UpdateKernel.AtomicWrite(Path.Combine(UpdateKernel.Root(plan.TargetDirectory), "state.json"), JsonSerializer.Serialize(state, UpdaterJsonContext.Default.KernelState));
            }
            progress?.Report(new UpdateProgress("正在下载更新包…", 5, plan.Version, UpdateStage.Downloading));

            if (localPackage is null) await DownloadPackageAsync(plan, packagePath, progress, cancellationToken);
            else packagePath = localPackage;
            progress?.Report(new UpdateProgress("正在校验更新包完整性…", 48, plan.Version, UpdateStage.Verifying));
            await VerifySha256Async(packagePath, plan.Sha256, cancellationToken);

            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            Directory.CreateDirectory(stagingDirectory);
            progress?.Report(new UpdateProgress("正在准备更新载荷…", 51, plan.Version, UpdateStage.Verifying));
            if (plan.PackageFormat is not ("auto" or "zip" or "cts-setup-v1")) throw new InvalidDataException("不支持此更新包格式。");
            if (plan.PackageFormat == "cts-setup-v1" && !SetupPackage.IsExecutable(packagePath)) throw new InvalidDataException("更新清单声明的安装包格式与内容不符。");
            if (plan.PackageFormat == "zip" && SetupPackage.IsExecutable(packagePath)) throw new InvalidDataException("更新清单声明的 ZIP 格式与内容不符。");
            if (SetupPackage.IsExecutable(packagePath))
            {
                var setup = SetupPackage.Read(packagePath);
                if (setup.Version != plan.Version || setup.Line != plan.TargetLine || setup.CompatibilityEpoch != plan.TargetCompatibilityEpoch)
                    throw new InvalidDataException("安装包版本线、版本号或兼容级别与更新计划不一致。");
                if (localPackage is not null) SetupPackage.Extract(packagePath, stagingDirectory, plan.Version);
                else await PrepareInstallerAsync(packagePath, stagingDirectory, plan.Version, cancellationToken);
                SetupPackage.ValidateDirectory(stagingDirectory, setup);
            }
            else ExtractZipSafely(packagePath, stagingDirectory);
            var payloadDirectory = ResolvePayloadDirectory(stagingDirectory);
            if (!Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("更新包中没有可安装文件。");

            progress?.Report(new UpdateProgress("正在检查目标运行时与兼容组件…", 55, plan.Version, UpdateStage.Adapting));
            var bundle = UpdateKernel.ValidateBundle(payloadDirectory);
            // Verify the retained private cache too; a corrupted cache must fail
            // before replacing any application file.
            var retainedBundle = UpdateKernel.ValidateBundle(plan.TargetDirectory,
                Environment.GetEnvironmentVariable("CTS_UPDATER_SLOT") ?? "engines/" + UpdateKernel.EngineVersion)
                ?? throw new InvalidDataException("持久更新组件清单缺失，请使用完整发布包修复 .Updater。");
            UpdateKernel.StageRuntimeCache(plan.TargetDirectory, payloadDirectory, bundle);
            var runtime = RuntimeAdapter.Preflight(plan, payloadDirectory);
            if (bundle is not null && Version.Parse(bundle.KernelVersion) > Version.Parse(state.KernelVersion))
                UpdateKernel.StageNewEngine(plan.TargetDirectory, payloadDirectory, bundle);
            progress?.Report(new UpdateProgress(runtime.Description, 58, plan.Version, UpdateStage.Adapting));
            backupDirectory = Path.Combine(UpdateKernel.Root(plan.TargetDirectory), "transactions", Guid.NewGuid().ToString("N"), "backup");
            state.PendingTransaction = Path.GetRelativePath(plan.TargetDirectory, backupDirectory);
            UpdateKernel.AtomicWrite(Path.Combine(UpdateKernel.Root(plan.TargetDirectory), "state.json"), JsonSerializer.Serialize(state, UpdaterJsonContext.Default.KernelState));
            transaction = new FileTransaction(plan.TargetDirectory, backupDirectory);
            if (bundle is not null && Version.Parse(bundle.KernelVersion) >= Version.Parse(state.KernelVersion))
                transaction.Replace(Path.Combine(UpdateKernel.Root(payloadDirectory), "compat-host.exe"), ".Updater/compat-host.exe");
            if (bundle is not null && bundle.EntrySha256.Length == 64 && Version.Parse(bundle.KernelVersion) > Version.Parse(state.KernelVersion))
                transaction.Replace(Path.Combine(payloadDirectory, "update.exe"), "update.exe");
            await ApplyTransactionAsync(plan, payloadDirectory, transaction, progress, cancellationToken, state.OwnedFiles);
            RuntimeAdapter.ApplyHost(plan, runtime, transaction);
            if (bundle is not null && Version.Parse(bundle.KernelVersion) > Version.Parse(state.KernelVersion))
            {
                var combined = retainedBundle is null ? bundle : UpdateKernel.MergeBundle(retainedBundle, bundle);
                transaction.Write(".Updater/bundle.json", JsonSerializer.Serialize(combined, UpdaterJsonContext.Default.BundleManifest));
            }

            progress?.Report(new UpdateProgress("正在启动目标版本并验证主窗口…", 94, plan.Version, UpdateStage.Starting));
            await StartAndVerifyAsync(plan.MainExecutablePath, cancellationToken);
            state.OwnedFiles = Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(payloadDirectory, f)).Where(p => !UpdateKernel.IsProtected(p)).ToList();
            UpdateKernel.Commit(plan, state, bundle);
            InstallationService.RefreshRegisteredVersion(plan.TargetDirectory, plan.Version);
            transaction = null;
            progress?.Report(new UpdateProgress("版本切换完成，主程序已就绪。", 100, plan.Version, UpdateStage.Completed));
            TryDelete(planPath);
            return new UpdateResult(true, $"已成功更新到 {plan.Version}，主程序正在启动。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Rollback("更新已取消。", null);
        }
        catch (Exception ex)
        {
            WriteLog(planPath, ex);
            return Rollback($"更新失败：{ex.Message}", ex);
        }
        finally { installationLock?.Dispose(); }

        UpdateResult Rollback(string message, Exception? original)
        {
            try
            {
                transaction?.Rollback();
                if (transaction is not null && activePlan is not null && state is not null)
                {
                    state.PendingTransaction = "";
                    UpdateKernel.AtomicWrite(Path.Combine(UpdateKernel.Root(activePlan.TargetDirectory), "state.json"), JsonSerializer.Serialize(state, UpdaterJsonContext.Default.KernelState));
                }
                return new UpdateResult(false, message + "\n未完成的应用文件替换已回滚，用户配置和工作区保持不变。详细信息见 update.log。");
            }
            catch (Exception rollbackError)
            {
                WriteLog(planPath, new AggregateException(new[] { original, rollbackError }.OfType<Exception>()));
                return new UpdateResult(false, message + "\n回滚尚未完成：" + rollbackError.Message + "\n请关闭占用程序后重新运行更新器，事务备份已保留。");
            }
        }
    }

    private static async Task PrepareInstallerAsync(string package, string staging, string version, CancellationToken token)
    {
        // Windows does not launch a downloaded .bin as an executable. Keep the verified
        // bytes unchanged while assigning the proper extension for CreateProcess.
        var executable = Path.ChangeExtension(package, ".exe");
        File.Move(package, executable, true);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in new[] { "--update-stage", "--output", staging, "--expected-version", version }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("无法启动安装包更新协作模式。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            await process.WaitForExitAsync(waiting.Token);
            if (process.ExitCode != 0) throw new IOException($"安装包准备载荷失败（{process.ExitCode}）。");
        }
        catch
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }
            throw;
        }
        // The caller verifies staged bytes against the same trusted package manifest.
        File.Move(executable, package, true);
    }

    private static async Task<UpdatePlan> LoadAndValidatePlanAsync(string planPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(planPath)) throw new FileNotFoundException("更新计划不存在。", planPath);
        await using var stream = File.OpenRead(planPath);
        var plan = await JsonSerializer.DeserializeAsync(stream, UpdaterJsonContext.Default.UpdatePlan, cancellationToken)
                   ?? throw new InvalidDataException("更新计划内容无效。");

        if (plan.SchemaVersion != 2) throw new InvalidDataException($"不支持的更新计划版本：{plan.SchemaVersion}。");
        if (string.IsNullOrWhiteSpace(plan.Version)) throw new InvalidDataException("更新计划缺少版本号。");
        ValidateCompatibility(plan);
        if (!Uri.TryCreate(plan.PackageUrl, UriKind.Absolute, out var packageUri))
            throw new InvalidDataException("更新包地址无效。");
        if (packageUri.Scheme != Uri.UriSchemeHttps && !(plan.AllowInsecureHttp && packageUri.Scheme == Uri.UriSchemeHttp))
            throw new InvalidDataException("更新包必须使用 HTTPS；仅明确允许的受信任内网源可使用 HTTP。");
        if (plan.Sha256.Length != 64 || !plan.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("更新包必须提供有效的 SHA-256 摘要。");
        if (plan.PackageSize is <= 0 or > MaximumPackageBytes)
            throw new InvalidDataException("更新包大小超出允许范围。");

        plan.TargetDirectory = Path.GetFullPath(plan.TargetDirectory);
        plan.MainExecutablePath = Path.GetFullPath(plan.MainExecutablePath);
        var targetPrefix = plan.TargetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
        if (!plan.MainExecutablePath.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("主程序路径不在目标安装目录内。");
        if (!Directory.Exists(plan.TargetDirectory)) throw new DirectoryNotFoundException("目标安装目录不存在。");
        return plan;
    }

    public static void ValidateCompatibility(UpdatePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.CurrentLine) || string.IsNullOrWhiteSpace(plan.TargetLine))
            throw new InvalidDataException("更新计划缺少版本线信息。");
        if (!string.Equals(plan.CurrentLine, plan.TargetLine, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"禁止跨版本线更新：{plan.CurrentLine} -> {plan.TargetLine}。");
        if (plan.CurrentCompatibilityEpoch <= 0 || plan.TargetCompatibilityEpoch <= 0)
            throw new InvalidDataException("更新计划的兼容级别无效。");
        if (plan.TargetCompatibilityEpoch < plan.CurrentCompatibilityEpoch)
            throw new InvalidDataException(
                $"目标版本兼容级别 {plan.TargetCompatibilityEpoch} 低于当前级别 {plan.CurrentCompatibilityEpoch}，禁止回退。");
    }

    private static async Task DownloadPackageAsync(
        UpdatePlan plan,
        string packagePath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CarTerminalSimulation-Updater", "1.0"));
        using var response = await client.GetAsync(plan.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseLength = response.Content.Headers.ContentLength;
        if (responseLength is > MaximumPackageBytes)
            throw new InvalidDataException("服务器返回的更新包过大。");
        if (plan.PackageSize is { } expectedLength && responseLength is { } actualLength && expectedLength != actualLength)
            throw new InvalidDataException($"更新包大小不匹配，期望 {expectedLength} 字节，实际 {actualLength} 字节。");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaximumPackageBytes) throw new InvalidDataException("下载内容超过最大允许大小。");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            var expected = plan.PackageSize ?? responseLength;
            var percent = expected is > 0 ? 5 + Math.Min(40, total * 40d / expected.Value) : (double?)null;
            progress?.Report(new UpdateProgress($"正在下载更新包… {FormatBytes(total)}" + (expected is > 0 ? $" / {FormatBytes(expected.Value)}" : ""), percent, plan.Version, UpdateStage.Downloading));
        }

        if (plan.PackageSize is { } size && total != size)
            throw new InvalidDataException($"更新包下载不完整，期望 {size} 字节，实际 {total} 字节。");
    }

    private static async Task VerifySha256Async(string packagePath, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(packagePath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"更新包 SHA-256 校验失败。期望 {expected}，实际 {actual}。");
    }

    public static void ExtractZipSafely(string packagePath, string stagingDirectory)
    {
        var root = Path.GetFullPath(stagingDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        long expandedBytes = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (archive.Entries.Count > 100_000) throw new InvalidDataException("更新包文件数量超出限制。");
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName)) continue;
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("更新包解压体积超出限制。");
            var destination = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"更新包包含越界路径：{entry.FullName}");
            if (!paths.Add(destination)) throw new InvalidDataException($"更新包包含重复路径：{entry.FullName}");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static string ResolvePayloadDirectory(string stagingDirectory)
    {
        var rootFiles = Directory.GetFiles(stagingDirectory);
        var rootDirectories = Directory.GetDirectories(stagingDirectory);
        return rootFiles.Length == 0 && rootDirectories.Length == 1 ? rootDirectories[0] : stagingDirectory;
    }

    private static async Task WaitForParentExitAsync(int processId, CancellationToken cancellationToken)
    {
        if (processId <= 0) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(ParentExitTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await process.WaitForExitAsync(linked.Token);
        }
        catch (ArgumentException)
        {
            // The process already exited.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("主程序未能在两分钟内退出，已取消文件替换。");
        }
    }

    internal static async Task ApplyWithRollbackAsync(
        UpdatePlan plan,
        string payloadDirectory,
        string backupDirectory,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var transaction = new FileTransaction(plan.TargetDirectory, backupDirectory);
        try { await ApplyTransactionAsync(plan, payloadDirectory, transaction, progress, cancellationToken, []); }
        catch { transaction.Rollback(); throw; }
    }

    private static async Task ApplyTransactionAsync(UpdatePlan plan, string payloadDirectory, FileTransaction transaction,
        IProgress<UpdateProgress>? progress, CancellationToken cancellationToken, IReadOnlyCollection<string> previousFiles)
    {
        // Read the old deployment before overwriting its config/deps. A downgrade
        // to a framework-dependent package must not keep app-local runtime files.
        var obsoleteRuntime = UpdateDeploymentPlanner.GetObsoleteRuntimeFiles(
            plan.TargetDirectory, payloadDirectory, plan.MainExecutablePath);
        var delete = plan.Delete.Concat(obsoleteRuntime).Concat(previousFiles.Where(p => !File.Exists(UpdateKernel.SafePath(payloadDirectory, p))))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !UpdateKernel.IsProtected(p)).ToArray();
        var payloadFiles = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories)
            .Where(p => !UpdateKernel.IsProtected(Path.GetRelativePath(payloadDirectory, p))).ToArray();
        // Resolve every path before touching the installation.
        foreach (var path in delete) _ = ResolveTargetPath(plan.TargetDirectory, path);

        for (var index = 0; index < payloadFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = payloadFiles[index];
            var relative = Path.GetRelativePath(payloadDirectory, source);
            transaction.Replace(source, relative);
            progress?.Report(new UpdateProgress(
                $"正在安装文件 {index + 1}/{payloadFiles.Length}…",
                60 + (index + 1) * 30d / Math.Max(1, payloadFiles.Length),
                plan.Version, UpdateStage.Installing));
            await Task.Yield();
        }

        foreach (var relative in delete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Delete(relative);
        }
    }

    private static string ResolveTargetPath(string targetDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("更新文件路径不能是绝对路径。");
        var root = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(targetDirectory, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"更新文件越过安装目录：{relativePath}");
        return path;
    }

    internal static async Task StartAndVerifyAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("更新后找不到主程序。", executablePath);
        using var process = Process.Start(new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        }) ?? throw new InvalidOperationException("无法启动目标程序。");
        try
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(300, cancellationToken);
                process.Refresh();
                if (process.HasExited) throw new InvalidOperationException($"目标程序启动后退出（{process.ExitCode}），准备恢复上一版本。");
                if (process.MainWindowHandle != IntPtr.Zero && process.Responding && process.MainWindowTitle.Contains("车载定位终端模拟系统"))
                {
                    await Task.Delay(1500, cancellationToken);
                    process.Refresh();
                    if (!process.HasExited && process.Responding) return;
                }
            }
            throw new TimeoutException("目标程序未在一分钟内进入可响应主窗口。");
        }
        catch
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            throw;
        }
    }

    private static void WriteLog(string planPath, Exception exception)
    {
        try
        {
            var planDirectory = Path.GetDirectoryName(planPath) ?? AppContext.BaseDirectory;
            File.AppendAllText(Path.Combine(planDirectory, "update.log"), $"[{DateTimeOffset.Now:O}] {exception}\n\n");
        }
        catch
        {
            // Logging must not hide the update failure.
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):F1} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };

}
