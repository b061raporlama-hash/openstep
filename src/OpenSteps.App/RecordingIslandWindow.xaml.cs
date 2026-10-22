using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OpenSteps.App;

public partial class RecordingIslandWindow : Window
{
    private readonly Func<Task> _stopRecording;
    private readonly NativeMethodsForApp.WinEventCallback _foregroundChanged;
    private IntPtr _foregroundHook;
    private IntPtr _handle;
    private int _topmostUpdatePending;

    public RecordingIslandWindow(Func<Task> stopRecording)
    {
        _stopRecording = stopRecording;
        _foregroundChanged = ForegroundChanged;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            _handle = handle;
            IsExcludedFromCapture = NativeMethodsForApp.SetWindowDisplayAffinity(handle, 0x11);
            HwndSource.FromHwnd(handle)?.AddHook(PreventMouseActivation);
            _foregroundHook = NativeMethodsForApp.SetWinEventHook(3, 3, IntPtr.Zero, _foregroundChanged, 0, 0, 0);
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
            Top = area.Top + 8;
            EnsureTopmost();
        };
        IsVisibleChanged += (_, _) => EnsureTopmost();
        Closed += (_, _) =>
        {
            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethodsForApp.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            _handle = IntPtr.Zero;
        };
    }

    internal bool IsExcludedFromCapture { get; private set; }

    internal void UpdateStatus(int count, bool paused)
    {
        StepCountText.Text = $"{count} {(count == 1 ? "step" : "steps")}";
        StopLabel.Text = paused ? "Paused · Stop" : "Stop Recording";
    }

    internal void EnsureTopmost()
    {
        if (IsVisible && _handle != IntPtr.Zero)
        {
            // Raise within the topmost band without activating or moving the island.
            NativeMethodsForApp.SetWindowPos(_handle, new IntPtr(-1), 0, 0, 0, 0, 0x0213);
        }
    }

    private void ForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint time)
    {
        if (Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _topmostUpdatePending, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            Interlocked.Exchange(ref _topmostUpdatePending, 0);
            EnsureTopmost();
        }));
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        await _stopRecording();
    }

    private static IntPtr PreventMouseActivation(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) // WM_MOUSEACTIVATE: accept the click without taking foreground focus.
        {
            handled = true;
            return new IntPtr(3); // MA_NOACTIVATE
        }

        return IntPtr.Zero;
    }
}
