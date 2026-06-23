using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TerminalSimulation.Avalonia
{
    public partial class HttpWorkspaceControl : UserControl
    {
        public HttpWorkspaceControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void AddRequest_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is global::Avalonia.Controls.Control ctrl && ctrl.Tag is ViewModels.Utilities.HttpFolderViewModel folder)
            {
                ((ViewModels.Utilities.HttpWorkspaceViewModel)DataContext!).AddRequestCommand.Execute(folder);
            }
        }

        private void RemoveFolder_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is global::Avalonia.Controls.Control ctrl && ctrl.Tag is ViewModels.Utilities.HttpFolderViewModel folder)
            {
                ((ViewModels.Utilities.HttpWorkspaceViewModel)DataContext!).RemoveFolderCommand.Execute(folder);
            }
        }

        private void SelectRequest_Tapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
        {
            if (sender is global::Avalonia.Controls.Border border && border.Tag is ViewModels.Utilities.HttpRequesterViewModel req)
            {
                ((ViewModels.Utilities.HttpWorkspaceViewModel)DataContext!).SelectRequestCommand.Execute(req);
            }
        }

        private void RemoveRequest_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ViewModels.Utilities.HttpRequesterViewModel req)
            {
                ((ViewModels.Utilities.HttpWorkspaceViewModel)DataContext!).RemoveRequestCommand.Execute(req);
            }
        }

        private void RenameTextBox_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == global::Avalonia.Input.Key.Enter && sender is global::Avalonia.Controls.TextBox tb)
            {
                if (tb.DataContext is ViewModels.Utilities.HttpFolderViewModel folder) folder.IsRenaming = false;
                else if (tb.DataContext is ViewModels.Utilities.HttpRequesterViewModel req) req.IsRenaming = false;
            }
        }

        private void RenameTextBox_LostFocus(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is global::Avalonia.Controls.TextBox tb)
            {
                if (tb.DataContext is ViewModels.Utilities.HttpFolderViewModel folder) folder.IsRenaming = false;
                else if (tb.DataContext is ViewModels.Utilities.HttpRequesterViewModel req) req.IsRenaming = false;
            }
        }

        private void RenameButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is global::Avalonia.Controls.Control ctrl)
            {
                if (ctrl.DataContext is ViewModels.Utilities.HttpFolderViewModel folder) folder.IsRenaming = true;
                else if (ctrl.DataContext is ViewModels.Utilities.HttpRequesterViewModel req) req.IsRenaming = true;
            }
        }
    }
}
