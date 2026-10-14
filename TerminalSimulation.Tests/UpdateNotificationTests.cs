using TerminalSimulation.Wpf;
using TerminalSimulation.Wpf.Services;
using TerminalSimulation.Wpf.Services.Updates;
using TerminalSimulation.Wpf.ViewModels;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class UpdateNotificationTests
{
    [Theory]
    [InlineData("preview10", "preview11", true)]
    [InlineData("preview10", "preview10", false)]
    [InlineData("preview10", "preview9", false)]
    [InlineData("preview10-build.20261014.abcd", "preview10-build.20261014.1", false)]
    [InlineData("v1.1", "v1.2", true)]
    [InlineData("v1.1", "v0.1", false)]
    public void OnlyUpgrades_ProduceBubble(string current, string available, bool expected)
        => Assert.Equal(expected, new UpdateNotificationPolicy().TryAnnounce(current, available, true, false));

    [Fact]
    public void SameRelease_IsAnnouncedOnce_InteractiveChecksDoNotPromptAgain()
    {
        var policy = new UpdateNotificationPolicy();
        Assert.True(policy.TryAnnounce("preview10", "preview11", true, false));
        Assert.False(policy.TryAnnounce("preview10", "Preview11", true, false));
        Assert.False(policy.TryAnnounce("preview10", " preview11 ", true, false));
        Assert.False(policy.TryAnnounce("preview10", "preview12", true, true));
        Assert.False(policy.TryAnnounce("preview10", "preview12", true, false));
        Assert.True(policy.TryAnnounce("preview10", "preview13", true, false));
    }

    [Fact]
    public void MutedOrEmptyResult_DoesNotPrompt_ReenableAllowsNewCheck()
    {
        var policy = new UpdateNotificationPolicy();
        Assert.False(policy.TryAnnounce("preview10", null, true, false));
        Assert.False(policy.TryAnnounce("preview10", "preview11", false, false));
        Assert.True(policy.TryAnnounce("preview10", "preview11", true, false));
    }

    [Fact]
    public async Task NotificationPreference_Persists_AndOldConfigDefaultsToEnabled()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cts-update-notice-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.json");
        var store = new AtomicJsonConfigStore<AppConfig>(path);
        try
        {
            Assert.True(store.Load().ShowUpdateNotifications);
            await store.SaveAsync(new AppConfig { ShowUpdateNotifications = false });
            Assert.False(new AtomicJsonConfigStore<AppConfig>(path).Load().ShowUpdateNotifications);
            Assert.True(System.Text.Json.JsonSerializer.Deserialize<AppConfig>("{}")!.ShowUpdateNotifications);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void BubblePlacement_AlignsPointerWithSettingsButton()
    {
        var popup = new System.Windows.Size(354, 190); var button = new System.Windows.Size(44, 40);
        var placement = Assert.Single(MainWindow.PlaceUpdateNotification(popup, button, new(0, 0)));
        Assert.Equal(button.Width / 2, placement.Point.X + popup.Width - 58);
        Assert.True(placement.Point.Y > button.Height);
    }
}
