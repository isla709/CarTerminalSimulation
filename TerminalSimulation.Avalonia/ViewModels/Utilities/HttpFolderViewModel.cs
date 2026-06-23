using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalSimulation.Avalonia.ViewModels.Utilities
{
    public partial class HttpFolderViewModel : ObservableObject
    {
        [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
        [ObservableProperty] private string _name = "新建分组";
        [ObservableProperty] private bool _isExpanded = true;
        [ObservableProperty] private bool _isRenaming = false;
        
        public ObservableCollection<HttpRequesterViewModel> Requests { get; } = new();
    }
}
