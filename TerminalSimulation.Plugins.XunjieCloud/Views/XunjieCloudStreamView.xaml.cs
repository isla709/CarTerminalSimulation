using System.Windows;
using System.Windows.Controls;
using TerminalSimulation.PluginBase;

namespace TerminalSimulation.Plugins.XunjieCloud.Views
{
    public partial class XunjieCloudStreamView : UserControl
    {
        public XunjieCloudStreamView()
        {
            InitializeComponent();
            SizeChanged += (_, _) => UpdateResponsiveLayout();
            Loaded += (_, _) => UpdateResponsiveLayout();
        }

        private void UpdateResponsiveLayout()
        {
            bool stacked = ResponsiveLayoutPolicy.GetMode(ActualWidth) == ResponsiveLayoutMode.Compact;
            Grid.SetRow(VideoCard, 0);
            Grid.SetColumn(VideoCard, 0);
            Grid.SetColumnSpan(VideoCard, stacked ? 3 : 1);

            Grid.SetRow(ControlsCard, stacked ? 2 : 0);
            Grid.SetColumn(ControlsCard, stacked ? 0 : 2);
            Grid.SetColumnSpan(ControlsCard, stacked ? 3 : 1);

            VideoRow.Height = stacked ? new GridLength(9, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
            LayoutGapRow.Height = stacked ? new GridLength(8) : new GridLength(0);
            ControlsRow.Height = stacked ? new GridLength(11, GridUnitType.Star) : new GridLength(0);
            ControlsColumn.MinWidth = stacked ? 0 : 250;
            VideoColumn.Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star);
            LayoutGapColumn.Width = stacked ? new GridLength(0) : new GridLength(8);
            ControlsColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }
    }
}
