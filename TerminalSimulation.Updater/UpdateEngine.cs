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
    {
        var workingDirectory = Path.Combine(Path.GetDirectoryName(planPath)!, "work");
        var packagePath = Path.Combine(workingDirectory, "package.zip");
        var stagingDirectory = Path.Combine(workingDirectory, "staging");
        var backupDirectory = Path.Combine(workingDirectory, "backup");

        try
        {
            Directory.CreateDirectory(workingDirectory);
            var plan = await LoadAndValidatePlanAsync(planPath, cancellationToken);
            progress?.Report(new UpdateProgress("正在等待主程序安全退出…", null, plan.Version));
            await WaitForParentExitAsync(plan.ParentProcessId, cancellationToken);
            progress?.Report(new UpdateProgress("正在下载更新包…", 0, plan.Version));

            await DownloadPackageAsync(plan, packagePath, progress, cancellationToken);
            progress?.Report(new UpdateProgress("正在校验更新包完整性…", null, plan.Version));
            await VerifySha256Async(packagePath, plan.Sha256, cancellationToken);

            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            Directory.CreateDirectory(stagingDirectory);
            progress?.Report(new UpdateProgress("正在安全解压更新包…", null, plan.Version));
            ExtractZipSafely(packagePath, stagingDirectory);
            var payloadDirectory = ResolvePayloadDirectory(stagingDirectory);
            if (!Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("更新包中没有可安装文件。");

            progress?.Report(new UpdateProgress("正在备份并替换程序文件…", null, plan.Version));
            await ApplyWithRollbackAsync(plan, payloadDirectory, backupDirectory, progress, cancellationToken);

            progress?.Report(new UpdateProgress("更新完成，正在重新启动…", 100, plan.Version));
            StartMainApplication(plan.MainExecutablePath);
            TryDelete(planPath);
            return new UpdateResult(true, $"已成功更新到 {plan.Version}，主程序正在启动。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UpdateResult(false, "更新已取消，现有程序未被破坏。");
        }
        catch (Exception ex)
        {
            WriteLog(planPath, ex);
            return new UpdateResult(false, $"更新失败：{ex.Message}\n现有文件已尽可能回滚。详细信息见 update.log。");
        }
    }

    private static async Task<UpdatePlan> LoadAndValidatePlanAsync(string planPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(planPath)) throw new FileNotFoundException("更新计划不存在。", planPath);
        await using var stream = File.OpenRead(planPath);
        var plan = await JsonSerializer.DeserializeAsync(stream, UpdaterJsonContext.Default.UpdatePlan, cancellationToken)
                   ?? throw new InvalidDataException("更新计划内容无效。");

        if (plan.SchemaVersion != 1) throw new InvalidDataException($"不支持的更新计划版本：{plan.SchemaVersion}。");
        if (string.IsNullOrWhiteSpace(plan.Version)) throw new InvalidDataException("更新计划缺少版本号。");
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
            var percent = expected is > 0 ? Math.Min(99, total * 100d / expected.Value) : (double?)null;
            progress?.Report(new UpdateProgress($"正在下载更新包… {FormatBytes(total)}", percent, plan.Version));
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
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName)) continue;
            var destination = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"更新包包含越界路径：{entry.FullName}");

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

    private static async Task ApplyWithRollbackAsync(
        UpdatePlan plan,
        string payloadDirectory,
        string backupDirectory,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(backupDirectory);
        var payloadFiles = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories);
        var appliedFiles = new List<AppliedFile>(payloadFiles.Length + plan.Delete.Count);

        try
        {
            for (var index = 0; index < payloadFiles.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = payloadFiles[index];
                var relative = Path.GetRelativePath(payloadDirectory, source);
                var destination = ResolveTargetPath(plan.TargetDirectory, relative);
                var backup = Path.Combine(backupDirectory, relative);
                var existed = File.Exists(destination);
                if (existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, overwrite: true);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".updating-" + Guid.NewGuid().ToString("N");
                File.Copy(source, temporary, overwrite: true);
                File.Move(temporary, destination, overwrite: true);
                appliedFiles.Add(new AppliedFile(destination, backup, existed));
                progress?.Report(new UpdateProgress(
                    $"正在安装文件 {index + 1}/{payloadFiles.Length}…",
                    75 + (index + 1) * 24d / Math.Max(1, payloadFiles.Length),
                    plan.Version));
                await Task.Yield();
            }

            foreach (var relative in plan.Delete.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var destination = ResolveTargetPath(plan.TargetDirectory, relative);
                if (!File.Exists(destination)) continue;
                var backup = Path.Combine(backupDirectory, "deleted", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, overwrite: true);
                File.Delete(destination);
                appliedFiles.Add(new AppliedFile(destination, backup, Existed: true));
            }
        }
        catch
        {
            for (var index = appliedFiles.Count - 1; index >= 0; index--)
            {
                var item = appliedFiles[index];
                try
                {
                    if (item.Existed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                        File.Copy(item.Backup, item.Destination, overwrite: true);
                    }
                    else
                    {
                        TryDelete(item.Destination);
                    }
                }
                catch
                {
                    // Continue restoring the remaining files; the primary exception is preserved.
                }
            }
            throw;
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

    private static void StartMainApplication(string executablePath)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("更新后找不到主程序。", executablePath);
        Process.Start(new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        });
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

    private sealed record AppliedFile(string Destination, string Backup, bool Existed);
}
