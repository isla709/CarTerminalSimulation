using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TerminalSimulation.Avalonia.Helpers;

public static class DialogHelper
{
    public static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            return TopLevel.GetTopLevel(singleView.MainView);
        }
        return null;
    }

    public static async Task<IStorageFile?> ShowOpenFileDialogAsync(string title, IReadOnlyList<FilePickerFileType> filters)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
        });

        if (files != null && files.Count > 0)
        {
            return files[0];
        }
        return null;
    }

    public static async Task<IStorageFile?> ShowSaveFileDialogAsync(string title, string defaultExtension, IReadOnlyList<FilePickerFileType> filters)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        return await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = filters
        });
    }
}
