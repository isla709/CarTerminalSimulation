using System;
using System.Diagnostics;
using System.IO;

namespace TerminalSimulation.Wpf.Helpers
{
    public static class PathHelper
    {
        /// <summary>
        /// The directory where the application is extracted/running from.
        /// Useful for reading embedded/bundled content files (like ffmpeg, Regions.json, Themes).
        /// </summary>
        public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// The directory where the executable file itself is located.
        /// Useful for saving mutable user data (like config.json, utility_settings.json, generated files)
        /// so they persist across runs when packaged as a Single File app.
        /// </summary>
        public static string ExeDir
        {
            get
            {
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        return Path.GetDirectoryName(exePath) ?? AppDir;
                    }
                }
                catch
                {
                    // Fallback if MainModule access fails
                }
                return AppDir;
            }
        }
    }
}
