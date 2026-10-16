using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace TerminalSimulation.Updater;

internal sealed class InstallationRecord
{
    public string Product { get; set; } = "TerminalSimulation";
    public string Directory { get; set; } = "";
    public string Version { get; set; } = "";
    public bool DesktopShortcut { get; set; }
    public bool StartMenuShortcut { get; set; } = true;
    public List<string> Files { get; set; } = [];
}

internal static class InstallationService
{
    internal const string RecordPath = ".Installation/install.json";
    private static string? QaId => Guid.TryParse(Environment.GetEnvironmentVariable("CTS_INSTALLER_QA_ID"), out var id) ? id.ToString("N") : null;
    private static string RegistryPath => QaId is { } id ? @"Software\CarTerminalSimulation\InstallerQA\" + id : @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CarTerminalSimulation";
    private static string ShortcutFolder(bool desktop) => QaId is { } id ? Path.Combine(Path.GetTempPath(), "CTS-InstallerQA", id, desktop ? "Desktop" : "Programs")
        : Environment.GetFolderPath(desktop ? Environment.SpecialFolder.DesktopDirectory : Environment.SpecialFolder.Programs);
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CarTerminalSimulation");
    internal static string PreferredDirectory
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue("InstallLocation") is string path && Directory.Exists(path) ? path : DefaultDirectory; }
    }

    internal static string ValidateTarget(string target)
    {
        var full = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent) || full.Equals(Path.GetPathRoot(full)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("不能将磁盘根目录作为安装目录。");
        foreach (var folder in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.Windows })
            if (full.Equals(Environment.GetFolderPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择专用的程序子目录。");
        if (Directory.Exists(Path.Combine(full, ".git")) || (Directory.Exists(full) && Directory.GetFiles(full, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0))
            throw new InvalidDataException("不能覆盖源代码项目目录。");
        // Refuse links in every existing ancestor, even when the final directory is new.
        for (var path = full; path is not null; path = Path.GetDirectoryName(path))
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("安装目录不能经过目录链接或重解析点。");
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any() &&
            !File.Exists(Path.Combine(full, "TerminalSimulation.Wpf.exe")) && !Directory.Exists(Path.Combine(full, ".Updater")))
            throw new InvalidDataException("所选目录非空且不是本程序目录，请选择专用空目录。");
        return full;
    }

    internal static bool IsProgramFilesTarget(string target)
    {
        var full = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath).Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))
            .Any(root => full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    internal static void PrepareProgramFilesTarget(string target, string userSid)
    {
        target = ValidateTarget(target);
        if (!IsProgramFilesTarget(target)) throw new InvalidOperationException("管理员授权仅用于 Program Files 下的专用安装目录。");
        var sid = new SecurityIdentifier(userSid);
        Directory.CreateDirectory(target);
        var directory = new DirectoryInfo(target);
        var security = FileSystemAclExtensions.GetAccessControl(directory, AccessControlSections.Access);
        security.SetAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Modify | FileSystemRights.DeleteSubdirectoriesAndFiles,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(directory, security);
    }

    internal static InstallationRecord? Read(string target)
    {
        var file = UpdateKernel.SafePath(target, RecordPath);
        if (!File.Exists(file)) return null;
        var record = JsonSerializer.Deserialize<InstallationRecord>(File.ReadAllText(file), SetupPackage.Json)
            ?? throw new InvalidDataException("安装记录损坏，请运行安装程序修复。");
        if (record.Product != "TerminalSimulation" || !Path.GetFullPath(record.Directory).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装记录与程序目录不一致。");
        return record;
    }

    internal static void EnsureApplicationClosed(string target)
    {
        foreach (var process in Process.GetProcessesByName("TerminalSimulation.Wpf"))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } path && Path.GetDirectoryName(path)!.Equals(target, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("请先关闭该目录正在运行的主程序，再重试。");
                }
                catch (System.ComponentModel.Win32Exception) { /* An unrelated user's process is not ours. */ }
            }
        }
    }

    internal static async Task<UpdateResult> InstallAsync(string package, string target, bool desktop, bool startMenu,
        IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        target = ValidateTarget(target);
        EnsureApplicationClosed(target);
        var metadata = SetupPackage.Read(package);
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(target, ".Installation"));
        using var setupLock = new FileStream(Path.Combine(target, ".Installation/setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RecoverInterruptedUninstall(target);
        var oldState = UpdateKernel.ReadState(target);
        var plan = new UpdatePlan { Version = metadata.Version, CurrentLine = oldState.Line.Length > 0 ? oldState.Line : metadata.Line,
            TargetLine = metadata.Line, CurrentCompatibilityEpoch = Math.Max(1, oldState.HighestCompatibilityEpoch), TargetCompatibilityEpoch = metadata.CompatibilityEpoch,
            TargetDirectory = target, MainExecutablePath = Path.Combine(target, metadata.MainExecutable), PackageUrl = "https://localhost/offline-setup",
            PackageSize = new FileInfo(package).Length };
        UpdateEngine.ValidateCompatibility(plan);
        UpdateKernel.ValidateState(plan, oldState);
        var session = Path.Combine(Path.GetTempPath(), "CarTerminalSimulation", "Installation", Guid.NewGuid().ToString("N"));
        var prepared = Path.Combine(session, "payload");
        Directory.CreateDirectory(session);
        progress?.Report(new("正在检查安装包…", 5, metadata.Version, UpdateStage.Verifying));
        SetupPackage.Extract(package, prepared);
        token.ThrowIfCancellationRequested();
        using var source = File.OpenRead(package);
        plan.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
        var bootstrap = new FileTransaction(target, Path.Combine(target, ".Updater/transactions", Guid.NewGuid().ToString("N"), "backup"));
        var initialized = false;
        try
        {
            var repairBootstrap = !File.Exists(Path.Combine(target, ".Updater/bundle.json"));
            if (!repairBootstrap)
            {
                try { _ = UpdateKernel.ValidateBundle(target); }
                catch
                {
                    var incoming = UpdateKernel.ValidateBundle(prepared)!;
                    if (Version.Parse(incoming.KernelVersion) < Version.Parse(oldState.KernelVersion))
                        throw new InvalidDataException("已安装的更新内核损坏，需使用相同或更高内核版本的安装包修复。");
                    repairBootstrap = true;
                }
            }
            if (repairBootstrap)
            {
                initialized = true;
                using var compatibilityLock = new FileStream(UpdateKernel.SafePath(target, ".Updater/update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                // Seed only the compatibility closure, never user/application files.
                // Native entry is written last so a partially copied engine is not launched.
                foreach (var file in Directory.GetFiles(Path.Combine(prepared, ".Updater"), "*", SearchOption.AllDirectories))
                    bootstrap.Replace(file, Path.GetRelativePath(prepared, file));
                foreach (var name in new[] { "update.dll", "update.deps.json", "update.runtimeconfig.json", "update.exe" })
                    bootstrap.Replace(Path.Combine(prepared, name), name);
            }
            var planPath = Path.Combine(session, "install-plan.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, UpdaterJsonContext.Default.UpdatePlan), token);
            var result = await new UpdateEngine().RunLocalAsync(planPath, package, progress, token);
            if (!result.Success) { if (initialized) bootstrap.Rollback(); return result; }
            initialized = false; // Application transaction is committed; never undo its bootstrap afterward.
            var record = new InstallationRecord { Directory = target, Version = metadata.Version, DesktopShortcut = desktop,
                StartMenuShortcut = startMenu, Files = metadata.Files.Keys.ToList() };
            try
            {
                File.Copy(Path.Combine(target, "update.exe"), Path.Combine(target, "uninstall.exe"), true);
                UpdateKernel.AtomicWrite(UpdateKernel.SafePath(target, RecordPath), JsonSerializer.Serialize(record, SetupPackage.Json));
                Register(record);
            }
            catch (Exception ex) { return new(true, "程序已安装并启动，但快捷方式/系统卸载登记未能完成。可再次运行安装包修复。\n" + ex.Message); }
            return new(true, "安装完成，主程序已启动。配置、插件和工作区已保留。");
        }
        catch { if (initialized) bootstrap.Rollback(); throw; }
    }

    internal static void RefreshRegisteredVersion(string target, string version)
    {
        // Registration failure must never roll back an already committed application transaction.
        try
        {
            var record = Read(target); if (record is null) return;
            record.Version = version;
            var current = UpdateKernel.ReadState(target).OwnedFiles;
            record.Files = record.Files.Concat(current).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            UpdateKernel.AtomicWrite(UpdateKernel.SafePath(target, RecordPath), JsonSerializer.Serialize(record, SetupPackage.Json));
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key is not null && string.Equals(key.GetValue("InstallLocation") as string, target, StringComparison.OrdinalIgnoreCase)) key.SetValue("DisplayVersion", version);
        }
        catch (Exception ex) { try { File.AppendAllText(Path.Combine(target, ".Installation/registration.log"), ex + "\n"); } catch { } }
    }

    private static string Shortcut(string folder) => Path.Combine(folder, "车载定位终端模拟系统.lnk");
    private static void SetShortcut(string folder, string executable, bool enabled)
    {
        var path = Shortcut(folder);
        if (!enabled) { DeleteOwnedShortcut(path, executable); return; }
        Directory.CreateDirectory(folder);
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("系统快捷方式服务不可用。");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            try { link.TargetPath = executable; link.WorkingDirectory = Path.GetDirectoryName(executable); link.IconLocation = executable + ",0"; link.Description = "车载定位终端模拟系统"; link.Save(); }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); }
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    private static void DeleteOwnedShortcut(string path, string executable)
    {
        if (!File.Exists(path)) return;
        var type = Type.GetTypeFromProgID("WScript.Shell")!; dynamic shell = Activator.CreateInstance(type)!;
        try { dynamic link = shell.CreateShortcut(path); try { if (string.Equals((string)link.TargetPath, executable, StringComparison.OrdinalIgnoreCase)) File.Delete(path); }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); } }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    private static void Register(InstallationRecord record)
    {
        var executable = Path.Combine(record.Directory, "TerminalSimulation.Wpf.exe");
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("DisplayName", "车载定位终端模拟系统"); key.SetValue("DisplayVersion", record.Version);
        key.SetValue("InstallLocation", record.Directory); key.SetValue("DisplayIcon", executable + ",0");
        key.SetValue("UninstallString", "\"" + Path.Combine(record.Directory, "uninstall.exe") + "\"");
        key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
        SetShortcut(ShortcutFolder(true), executable, record.DesktopShortcut);
        SetShortcut(ShortcutFolder(false), executable, record.StartMenuShortcut);
    }

    internal static bool IsUserData(string relative)
    {
        var p = relative.Replace('\\', '/');
        return p.StartsWith("Workspaces/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith(".Installation/", StringComparison.OrdinalIgnoreCase) || p.Contains("/transactions/", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            new[] { "config.json", "utility_settings.json", "update-sources.json" }.Contains(p, StringComparer.OrdinalIgnoreCase);
    }

    internal static void RecoverInterruptedUninstall(string target)
    {
        var marker = UpdateKernel.SafePath(target, ".Installation/uninstall-pending.txt");
        if (!File.Exists(marker)) return;
        using var recoveryLock = new FileStream(UpdateKernel.SafePath(target, ".Updater/update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        FileTransaction.Recover(target, File.ReadAllText(marker));
        File.Delete(marker);
    }

    internal static Task<UpdateResult> UninstallAsync(string target, IProgress<UpdateProgress>? progress, CancellationToken token) => Task.Run(() =>
    {
        target = Path.GetFullPath(target); EnsureApplicationClosed(target);
        using var setupLock = new FileStream(UpdateKernel.SafePath(target, ".Installation/setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RecoverInterruptedUninstall(target);
        var record = Read(target) ?? throw new InvalidDataException("所选目录没有安装记录。");
        using var updateLock = new FileStream(UpdateKernel.SafePath(target, ".Updater/update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var state = UpdateKernel.ReadState(target);
        if (state.PendingTransaction.Length > 0) throw new IOException("存在未恢复的更新事务，请先完成恢复再卸载。");
        var bundle = UpdateKernel.ValidateBundle(target);
        var paths = record.Files.Concat(state.OwnedFiles).Concat(bundle?.Files.Keys.Select(x => ".Updater/" + x) ?? [])
            .Concat(new[] { "uninstall.exe", "update.exe", "update.dll", "update.deps.json", "update.runtimeconfig.json", ".Updater/current.txt", ".Updater/last-good.txt", ".Updater/bundle.json", ".Updater/state.json", ".Updater/app-runtime.txt", ".Updater/app-hostfxr.txt" })
            .Where(p => !IsUserData(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in paths) _ = UpdateKernel.SafePath(target, path);
        var backup = Path.Combine(target, ".Updater/transactions", Guid.NewGuid().ToString("N"), "backup");
        var transaction = new FileTransaction(target, backup);
        var pending = UpdateKernel.SafePath(target, ".Installation/uninstall-pending.txt");
        UpdateKernel.AtomicWrite(pending, Path.GetRelativePath(target, backup));
        try
        {
            for (var i = 0; i < paths.Length; i++)
            { token.ThrowIfCancellationRequested(); transaction.Delete(paths[i]); progress?.Report(new($"正在移除程序文件 {i + 1}/{paths.Length}…", (i + 1) * 95d / paths.Length, record.Version, UpdateStage.Installing)); }
            var executable = Path.Combine(target, "TerminalSimulation.Wpf.exe");
            DeleteOwnedShortcut(Shortcut(ShortcutFolder(true)), executable);
            DeleteOwnedShortcut(Shortcut(ShortcutFolder(false)), executable);
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                if (string.Equals(key?.GetValue("InstallLocation") as string, target, StringComparison.OrdinalIgnoreCase)) Registry.CurrentUser.DeleteSubKeyTree(RegistryPath);
            File.Delete(UpdateKernel.SafePath(target, RecordPath));
            File.Delete(pending);
            return new UpdateResult(true, "程序已卸载。配置、插件、工作区及事务备份保留在原目录，未删除其他文件。");
        }
        catch { transaction.Rollback(); File.Delete(pending); throw; }
    }, token);
}
