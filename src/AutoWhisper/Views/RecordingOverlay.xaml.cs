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

    public void UpdateTimer(TimeSpan elapsed)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        });
    }
}
