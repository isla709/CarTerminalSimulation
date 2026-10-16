using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TerminalSimulation.Updater;

public partial class UpdaterWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private UpdateStage _stage;
    private double _targetProgress;
    private bool _finished;
    public CancellationToken CancellationToken => _cancellation.Token;
    private static bool Animate => SystemParameters.ClientAreaAnimation && SystemParameters.UIEffects;

    public UpdaterWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!Animate) return;
            LayoutRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            RingRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(8)) { RepeatBehavior = RepeatBehavior.Forever });
        };
        Unloaded += (_, _) => RingRotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    public void ReportProgress(UpdateProgress progress)
    {
        if (_finished) return;
        VersionText.Text = string.IsNullOrWhiteSpace(progress.Version) ? "正在准备安全更新" : $"目标版本  {progress.Version}";
        StatusText.Text = progress.Message;
        if (progress.Percent is { } percent) SetProgress(Math.Max(_targetProgress, Math.Clamp(percent, 0, 100)));
        if (_stage != progress.Stage || PhaseTitle.Text == "一切就绪，即将开始")
        {
            _stage = progress.Stage;
            PhaseTitle.Text = progress.Stage switch {
                UpdateStage.Downloading => "正在获取目标版本",
                UpdateStage.Verifying => "确保每一份文件可信",
                UpdateStage.Adapting => "为目标版本适配运行环境",
                UpdateStage.Installing => "正在安全替换应用文件",
                UpdateStage.Starting => "准备出发，正在验证启动",
                UpdateStage.Completed => "已就绪，欢迎回来",
                _ => "正在准备安全更新"
            };
            if (Animate)
            {
                PhaseContent.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(220)));
                PhaseTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            var steps = new[] { DownloadStep, VerifyStep, RuntimeStep, InstallStep, StartStep };
            var current = (int)progress.Stage - 1;
            for (var i = 0; i < steps.Length; i++)
            {
                steps[i].Background = new SolidColorBrush(i <= current ? Color.FromRgb(224, 237, 255) : Color.FromRgb(233, 237, 246));
                ((TextBlock)steps[i].Child).Foreground = new SolidColorBrush(i <= current ? Color.FromRgb(43, 111, 221) : Color.FromRgb(101, 115, 140));
            }
        }
    }

    private void SetProgress(double value)
    {
        // Update the base value first and replace previous animations: no snapback.
        var displayed = ProgressBar.Value;
        ProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
        _targetProgress = value;
        ProgressBar.Value = value;
        if (Animate && IsLoaded)
            ProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new DoubleAnimation(displayed, value, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        PercentText.Text = $"{value:F0}%";
    }

    public void ReportCompleted(UpdateResult result)
    {
        _finished = true;
        RingRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        StatusText.Text = result.Message;
        PhaseTitle.Text = result.Success ? "已就绪，欢迎回来" : (_cancellation.IsCancellationRequested ? "更新已安全取消" : "本次更新未能完成");
        CloseButton.IsEnabled = !result.Success;
        CloseButton.Content = result.Success ? "已完成" : "关闭";
        if (result.Success)
        {
            SetProgress(100);
            var green = new SolidColorBrush(Color.FromRgb(31, 169, 128));
            ProgressBar.Foreground = green;
            PercentText.Foreground = green;
            IconSurface.Background = green;
            foreach (var step in new[] { DownloadStep, VerifyStep, RuntimeStep, InstallStep, StartStep })
            {
                step.Background = new SolidColorBrush(Color.FromRgb(224, 245, 237));
                ((TextBlock)step.Child).Foreground = green;
            }
            UpdateIcon.Visibility = Visibility.Collapsed;
            SuccessMark.Visibility = Visibility.Visible;
            if (Animate && IsLoaded)
            {
                var duration = TimeSpan.FromMilliseconds(350);
                SuccessScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.55, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                SuccessScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.55, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
        else
        {
            ProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(224, 113, 75));
            PercentText.Foreground = ProgressBar.Foreground;
        }
        if (result.Success && IsLoaded)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(950) };
            timer.Tick += (_, _) => { timer.Stop(); Application.Current.Shutdown(); };
            timer.Start();
        }
    }

    private void RequestCancellation()
    {
        _cancellation.Cancel();
        CloseButton.IsEnabled = false;
        CloseButton.Content = "正在安全取消…";
        StatusText.Text = "正在安全取消更新，等待当前操作结束并恢复文件，请勿强制结束进程。";
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_finished) return;
        e.Cancel = true;
        RequestCancellation();
    }
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_finished) Close();
        else RequestCancellation();
    }
}
