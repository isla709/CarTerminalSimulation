using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TerminalSimulation.PluginBase;

namespace TerminalSimulation.Wpf.Plugins
{
    public class PluginManager
    {
        public List<IPlugin> LoadedPlugins { get; } = new List<IPlugin>();

        public void LoadPlugins(IPluginContext context)
        {
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
                return;
            }

            var pluginFiles = Directory.GetFiles(pluginsDir, "*.Plugin");
            foreach (var file in pluginFiles)
            {
                try
                {
                    var pluginHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                    context.Log($"[插件审计] 将以主进程权限加载 {Path.GetFullPath(file)} | SHA-256={pluginHash}");
                    // Load dependency assemblies from the Plugins folder if they are present there
                    // A proper AssemblyResolve event handler could be better, but LoadFrom generally works for simple cases.
                    var assembly = Assembly.LoadFrom(file);
                    var pluginTypes = assembly.GetTypes()
                        .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                    foreach (var type in pluginTypes)
                    {
                        if (Activator.CreateInstance(type) is IPlugin plugin)
                        {
                            plugin.Initialize(context);
                            LoadedPlugins.Add(plugin);
                        }
                    }
                }
                catch (Exception ex)
                {
                    context.Log($"[插件加载失败] {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
    }
}
