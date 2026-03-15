using System.Windows;

namespace AutoWhisper.Views;

public partial class RecordingOverlay : Window
{
    public RecordingOverlay()
    {
        InitializeComponent();
        PositionBottomCenter();
    }

    private void PositionBottomCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = 40;
    }

    public void ResetAndShow()
    {
        TimerText.Text = "0:00";
        PositionBottomCenter();
        Show();
    }

    public void UpdateTimer(TimeSpan elapsed)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Prevent actual close during normal operation — just hide instead
        // Only allow close during app shutdown
        if (Application.Current?.ShutdownMode == ShutdownMode.OnExplicitShutdown)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
