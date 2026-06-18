using System;
using System.IO;
using System.Reflection;

namespace TerminalSimulation.Avalonia.Helpers
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
                    string exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        return Path.GetDirectoryName(exePath) ?? AppDir;
                    }
                }
                catch
                {
                    // Fallback if path resolution fails
                }
                return AppDir;
            }
        }
    }
}
