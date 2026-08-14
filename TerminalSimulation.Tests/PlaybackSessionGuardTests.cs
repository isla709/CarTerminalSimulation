using TerminalSimulation.Plugins.XunjieCloud.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class PlaybackSessionGuardTests
{
    [Fact]
    public void Cancel_InvalidatesTokenAndGeneration()
    {
        using var guard = new PlaybackSessionGuard();
        var first = guard.Begin();
        Assert.True(guard.IsCurrent(first.Generation, first.Token));
        guard.Cancel();
        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(guard.IsCurrent(first.Generation, first.Token));
    }

    [Fact]
    public void NewSessionPreventsOldEventsFromUpdatingCurrentPlayback()
    {
        using var guard = new PlaybackSessionGuard();
        var first = guard.Begin();
        var second = guard.Begin();
        Assert.False(guard.IsCurrent(first.Generation, first.Token));
        Assert.True(guard.IsCurrent(second.Generation, second.Token));
    }

    [Fact]
    public async Task CanceledSessionCannotRunDelayedRetry()
    {
        using var guard = new PlaybackSessionGuard();
        var session = guard.Begin();
        guard.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(10, session.Token));
        Assert.False(guard.IsCurrent(session.Generation, session.Token));
    }
}
