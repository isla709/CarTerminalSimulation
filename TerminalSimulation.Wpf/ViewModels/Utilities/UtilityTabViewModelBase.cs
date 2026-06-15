using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TerminalSimulation.Wpf.ViewModels.Utilities
{
    public abstract partial class UtilityTabViewModelBase : ObservableObject, IDisposable
    {
        [ObservableProperty] private string _title = "新建工具";
        [ObservableProperty] private string _iconKind = "Toolbox";
        [ObservableProperty] private bool _isClosable = true;

        public event Action<UtilityTabViewModelBase>? RequestClose;

        [RelayCommand]
        private void Close()
        {
            RequestClose?.Invoke(this);
            Dispose();
        }

        public virtual void Dispose()
        {
            // Override in derived classes to clean up resources
        }
    }
}
