using System;
using System.Windows;
using TerminalSimulation.PluginBase;

namespace TerminalSimulation.Wpf.ViewModels.Utilities;

/// <summary>
/// Describes a tool that can be opened from the utilities picker. Built-in tools
/// and external plugins share this presentation model, while their creation and
/// lifecycle remain independent.
/// </summary>
public sealed class UtilityToolDefinition
{
    private readonly Func<FrameworkElement> _contentFactory;

    public UtilityToolDefinition(
        string id,
        string name,
        string description,
        string iconKind,
        bool allowMultipleInstances,
        bool isBuiltIn,
        Func<FrameworkElement> contentFactory)
    {
        Id = id;
        Name = name;
        Description = description;
        IconKind = iconKind;
        AllowMultipleInstances = allowMultipleInstances;
        IsBuiltIn = isBuiltIn;
        _contentFactory = contentFactory;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string IconKind { get; }
    public bool AllowMultipleInstances { get; }
    public bool IsBuiltIn { get; }

    public FrameworkElement CreateContent() => _contentFactory();

    public static UtilityToolDefinition FromPlugin(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        return new UtilityToolDefinition(
            $"plugin:{plugin.Id}",
            plugin.Name,
            plugin.Description,
            plugin.IconKind,
            plugin.AllowMultipleInstances,
            isBuiltIn: false,
            plugin.GetConfigurationPanel);
    }
}
