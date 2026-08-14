using System.Speech.Synthesis;

namespace TerminalSimulation.Wpf.Services;

internal sealed class SystemTtsService : ITtsService
{
    public IReadOnlyList<string> GetInstalledVoices()
    {
        using var synthesizer = new SpeechSynthesizer();
        return synthesizer.GetInstalledVoices().Where(voice => voice.Enabled).Select(voice => voice.VoiceInfo.Name).ToArray();
    }

    public Task SpeakAsync(string text, string? voiceName = null, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var synthesizer = new SpeechSynthesizer();
            if (!string.IsNullOrWhiteSpace(voiceName)) synthesizer.SelectVoice(voiceName);
            synthesizer.Speak(text);
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);
}
