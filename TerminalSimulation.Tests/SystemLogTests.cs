using System.Windows.Threading;
using TerminalSimulation.Wpf.Services;
using TerminalSimulation.Wpf.ViewModels;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class SystemLogTests
{
    [Theory]
    [InlineData("发送", ApplicationLogCategory.Communication)]
    [InlineData("接收解析", ApplicationLogCategory.Communication)]
    [InlineData("协议", ApplicationLogCategory.Communication)]
    [InlineData("网络", ApplicationLogCategory.Communication)]
    [InlineData("音视频控制", ApplicationLogCategory.Communication)]
    [InlineData("插件", ApplicationLogCategory.Plugin)]
    [InlineData("系统", ApplicationLogCategory.System)]
    [InlineData("地图", ApplicationLogCategory.System)]
    [InlineData("Update", ApplicationLogCategory.System)]
    [InlineData("配置", ApplicationLogCategory.System)]
    public void SourcesHaveExplicitCategories(string source, ApplicationLogCategory category) =>
        Assert.Equal(category, ApplicationLog.Classify(source));

    [Fact]
    public void HistoryIsBoundedOrderedAndRetainsRawCopy()
    {
        var log = new ApplicationLog();
        for (var i = 0; i < ApplicationLog.Capacity + 10; i++) log.Write("系统", i.ToString());
        var history = log.Snapshot();
        Assert.Equal(ApplicationLog.Capacity, history.Length);
        Assert.Equal("10", history[0].Message);
        Assert.True(history.Zip(history.Skip(1)).All(p => p.First.Sequence < p.Second.Sequence));
        log.Write("接收", "[connection=3] RAW: 7E 01 02 7E");
        Assert.Equal("7E 01 02 7E", log.Snapshot()[^1].RawData);
        log.Write("系统", "RAW: not hex");
        Assert.Empty(log.Snapshot()[^1].RawData);
    }

    [Fact]
    public void SingleCombinedAndEmptyFiltersNeverDeleteHistory() => RunSta(() =>
    {
        var log = new ApplicationLog();
        log.Write("发送", "RAW: 7E01"); log.Write("系统", "ready"); log.Write("插件", "plugin ready");
        using var model = new SystemLogViewModel(log, Dispatcher.CurrentDispatcher);
        Assert.Equal(3, model.VisibleEntries.Cast<object>().Count());
        model.ShowSystem = false; model.ShowPlugin = false;
        Assert.Equal(ApplicationLogCategory.Communication, Assert.Single(model.VisibleEntries.Cast<LogMessageItem>()).Category);
        log.Write("插件", "hidden plugin"); model.RefreshFromHistory();
        Assert.Single(model.VisibleEntries.Cast<object>());
        model.ShowPlugin = true;
        Assert.Equal(3, model.VisibleEntries.Cast<object>().Count());
        model.ShowCommunication = false;
        Assert.Equal(2, model.VisibleEntries.Cast<object>().Count());
        Assert.All(model.VisibleEntries.Cast<LogMessageItem>(), e => Assert.Equal(ApplicationLogCategory.Plugin, e.Category));
        model.ShowPlugin = false;
        Assert.True(model.VisibleEntries.IsEmpty);
        Assert.Equal(4, model.Entries.Count);
        model.ShowAllCategoriesCommand.Execute(null);
        Assert.Equal(4, model.VisibleEntries.Cast<object>().Count());
        model.Clear();
        Assert.Empty(model.Entries);
        Assert.Empty(log.Snapshot());
        log.Write("系统", "after clear"); model.RefreshFromHistory();
        Assert.Equal("after clear", Assert.Single(model.Entries).Message);
    });

    [Fact]
    public void BackgroundFloodAndClearOnlyShowLatestHistory() => RunSta(() =>
    {
        var log = new ApplicationLog();
        using var model = new SystemLogViewModel(log, Dispatcher.CurrentDispatcher);
        Task.Run(() => { for (var i = 0; i < 5000; i++) log.Write("接收", i.ToString()); }).GetAwaiter().GetResult();
        model.RefreshFromHistory();
        Assert.Equal(ApplicationLog.Capacity, model.Entries.Count);
        Assert.Equal("3000", model.Entries[0].Message);
        Task.Run(() => { log.Clear(); log.Write("插件", "after background clear"); }).GetAwaiter().GetResult();
        model.RefreshFromHistory();
        Assert.Equal("after background clear", Assert.Single(model.Entries).Message);
    });

    [Fact]
    public void DisposedViewDetachesFromHistory() => RunSta(() =>
    {
        var log = new ApplicationLog();
        var model = new SystemLogViewModel(log, Dispatcher.CurrentDispatcher);
        model.Dispose();
        log.Write("系统", "ignored"); model.RefreshFromHistory();
        Assert.Empty(model.Entries);
    });

    [Fact]
    public void BackgroundBurstIsDispatchedAsOneRefresh() => RunSta(() =>
    {
        var log = new ApplicationLog();
        using var model = new SystemLogViewModel(log, Dispatcher.CurrentDispatcher);
        var refreshes = 0;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.CountSummary)) refreshes++; };
        Task.Run(() => { for (var i = 0; i < 5000; i++) log.Write("接收", i.ToString()); }).GetAwaiter().GetResult();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        Assert.Equal(1, refreshes);
        Assert.Equal(ApplicationLog.Capacity, model.Entries.Count);
        Assert.Equal("4999", model.Entries[^1].Message);
    });

    [Fact]
    public void ConsoleAndServiceLogsReachHistoryWithoutConsoleSetup()
    {
        var marker = Guid.NewGuid().ToString("N");
        var logger = new AppLogger();
        logger.Info("发送", "RAW: 7E017E", connectionId: 991);
        TerminalSimulation.Wpf.ConsoleLogger.LogDebug("地图", marker + " map");
        logger.Info("插件", marker + " plugin");
        var history = ApplicationLog.Shared.Snapshot();
        var raw = Assert.Single(history, e => e.Message == "[connection=991] RAW: 7E017E");
        Assert.Equal(ApplicationLogCategory.Communication, raw.Category);
        Assert.Equal("7E017E", raw.RawData);
        Assert.Equal(ApplicationLogCategory.System, Assert.Single(history, e => e.Message == marker + " map").Category);
        Assert.Equal(ApplicationLogCategory.Plugin, Assert.Single(history, e => e.Message == marker + " plugin").Category);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "System log STA test timed out.");
        Assert.Null(failure);
    }
}
