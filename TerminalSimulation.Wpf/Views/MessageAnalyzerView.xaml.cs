using System.Windows.Controls;
using System.Windows.Input;

namespace TerminalSimulation.Wpf.Views;

public partial class MessageAnalyzerView : UserControl
{
    public MessageAnalyzerView() => InitializeComponent();

    private void AnalyzerTreeView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control &&
            sender is TreeView tree && tree.SelectedItem is ViewModels.AnalyzerNode node)
        {
            System.Windows.Clipboard.SetText($"{node.Name} {node.Value}".Trim());
            e.Handled = true;
        }
    }
}
