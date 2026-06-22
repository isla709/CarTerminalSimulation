using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TerminalSimulation.Avalonia.Helpers;

public static class CrossPlatformTts
{
    public static List<string> GetInstalledVoices()
    {
        var voices = new List<string>();

#pragma warning disable CA1416 // 验证平台兼容性
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var synth = new System.Speech.Synthesis.SpeechSynthesizer();
                foreach (var voice in synth.GetInstalledVoices())
                {
                    if (voice.Enabled)
                    {
                        voices.Add(voice.VoiceInfo.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetInstalledVoices error: {ex.Message}");
            }
        }
#pragma warning restore CA1416

        return voices;
    }

    public static void Speak(string text, string voiceName)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
#pragma warning disable CA1416 // 验证平台兼容性
                if (OperatingSystem.IsWindows())
                {
                    using var synth = new System.Speech.Synthesis.SpeechSynthesizer();
                    if (!string.IsNullOrEmpty(voiceName))
                    {
                        synth.SelectVoice(voiceName);
                    }
                    synth.Speak(text);
                }
#pragma warning restore CA1416
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("say", $"\"{text}\"");
                }
                else if (OperatingSystem.IsLinux())
                {
                    try
                    {
                        Process.Start("spd-say", $"\"{text}\"");
                    }
                    catch
                    {
                        Process.Start("espeak", $"\"{text}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CrossPlatformTts Speak error: {ex.Message}");
            }
        });
    }
}
