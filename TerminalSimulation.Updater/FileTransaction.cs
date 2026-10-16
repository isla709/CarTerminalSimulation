using System.Text.Json;

namespace TerminalSimulation.Updater;

/// <summary>A write-ahead, reverse-order file journal, retained until startup succeeds.</summary>
internal sealed class FileTransaction
{
    private readonly string _installation;
    private readonly string _backup;
    private readonly List<TransactionFile> _files = [];
    private string Journal => Path.Combine(_backup, "transaction.json");
    internal FileTransaction(string installation, string backup)
    {
        _installation = Path.GetFullPath(installation);
        _backup = Path.GetFullPath(backup);
        Directory.CreateDirectory(_backup);
    }

    internal void Replace(string source, string relative)
    {
        var destination = UpdateKernel.SafePath(_installation, relative);
        Capture(destination, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".updating-" + Guid.NewGuid().ToString("N");
        try { File.Copy(source, temporary, true); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal void Write(string relative, string text)
    {
        var destination = UpdateKernel.SafePath(_installation, relative);
        Capture(destination, relative);
        UpdateKernel.AtomicWrite(destination, text);
    }

    internal void Delete(string relative)
    {
        var destination = UpdateKernel.SafePath(_installation, relative);
        if (!File.Exists(destination)) return;
        Capture(destination, relative, "deleted/");
        File.Delete(destination);
    }

    private void Capture(string destination, string relative, string prefix = "")
    {
        if (_files.Any(f => f.Relative.Equals(relative, StringComparison.OrdinalIgnoreCase))) return;
        var exists = File.Exists(destination);
        var backup = prefix + relative.Replace('\\', '/');
        if (exists)
        {
            var file = UpdateKernel.SafePath(_backup, backup);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(destination, file, true);
        }
        _files.Add(new TransactionFile { Relative = relative, Backup = backup, Existed = exists });
        UpdateKernel.AtomicWrite(Journal, JsonSerializer.Serialize(_files, UpdaterJsonContext.Default.ListTransactionFile));
    }

    internal void Rollback()
    {
        var errors = new List<Exception>();
        foreach (var item in _files.AsEnumerable().Reverse())
        {
            try
            {
                var destination = UpdateKernel.SafePath(_installation, item.Relative);
                if (item.Existed) File.Copy(UpdateKernel.SafePath(_backup, item.Backup), destination, true);
                else if (File.Exists(destination)) File.Delete(destination);
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("部分文件仍被占用，回滚未完成；备份和日志已保留。", errors);
    }

    internal static void Recover(string installation, string relativeBackup)
    {
        if (!relativeBackup.Replace('\\', '/').StartsWith(".Updater/transactions/", StringComparison.Ordinal))
            throw new InvalidDataException("未完成事务路径无效。");
        var backup = UpdateKernel.SafePath(installation, relativeBackup);
        var transaction = new FileTransaction(installation, backup);
        if (!File.Exists(transaction.Journal)) return; // Crash before the first write.
        var entries = JsonSerializer.Deserialize(File.ReadAllText(transaction.Journal), UpdaterJsonContext.Default.ListTransactionFile)
                      ?? throw new InvalidDataException("未完成事务日志无效。");
        transaction._files.AddRange(entries);
        transaction.Rollback();
    }
}

internal sealed class TransactionFile
{
    public string Relative { get; set; } = "";
    public string Backup { get; set; } = "";
    public bool Existed { get; set; }
}
