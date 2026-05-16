using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace exui_wpf;

public partial class Speedometer : Window
{
    private Point _clickOffset; //click drag is manually implemented to bypass OS-level titlebar dragging limitations and allow dragging from any pixel on the window
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private MainWindow? _hostContext;

    // STATIC GLOW ENGINE: Completely isolated to this template to prevent clashes
    public static readonly SpeedometerGlowEngine Glow = new();

    private double _smoothedSpeed;

    public static readonly DependencyProperty SmoothedSpeedProperty = 
        DependencyProperty.Register(nameof(SmoothedSpeed), typeof(double), typeof(Speedometer), new PropertyMetadata(0.0));

    public double SmoothedSpeed
    {
        get => (double)GetValue(SmoothedSpeedProperty);
        set => SetValue(SmoothedSpeedProperty, value);
    }

    public static readonly DependencyProperty SmoothedAngleProperty = 
        DependencyProperty.Register(nameof(SmoothedAngle), typeof(double), typeof(Speedometer), new PropertyMetadata(0.0));

    public double SmoothedAngle
    {
        get => (double)GetValue(SmoothedAngleProperty);
        set => SetValue(SmoothedAngleProperty, value);
    }

    public Speedometer()
    {
        this.WindowStartupLocation = WindowStartupLocation.Manual;
        InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.SourceInitialized += (s, e) => UpdateClickThroughState();
        CompositionTarget.Rendering += OnRenderFrameFrameTick;
    }

    private void OnRenderFrameFrameTick(object? sender, EventArgs e)
    {
        if (_hostContext?.Telemetry == null) return;

        try
        {
            if (_hostContext.Telemetry["speed"] is object rawValue)
            {
                double targetSpeed = Convert.ToDouble(rawValue);
                
                // Smooths the live speed telemetry
                _smoothedSpeed += (targetSpeed - _smoothedSpeed) * 0.15;
                SmoothedSpeed = _smoothedSpeed;
                
                // NEW RATIO: 160 deg arc sweep / 210 Max MPH
                SmoothedAngle = _smoothedSpeed * (160.0 / 210.0);
            }
        }
        catch { }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_hostContext != null) _hostContext.ProgramState.PropertyChanged -= OnProgramStateChanged;
        if (e.NewValue is MainWindow host)
        {
            _hostContext = host;
            _hostContext.ProgramState.PropertyChanged += OnProgramStateChanged;
            UpdateClickThroughState();
        }
    }

    private void OnProgramStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProgramState.IsEditorMode)) UpdateClickThroughState();
    }

    private void UpdateClickThroughState()
    {
        if (_hostContext == null) return;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, _hostContext.ProgramState.IsEditorMode ? (extendedStyle & ~WS_EX_TRANSPARENT) : (extendedStyle | WS_EX_TRANSPARENT));
    }


    private void Window_CustomMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_hostContext?.ProgramState.IsEditorMode == true && e.ChangedButton == MouseButton.Left)
        {
            // Grab the exact anchor point relative to the window in pure DIPs
            _clickOffset = e.GetPosition(this);
            this.CaptureMouse();
        }
    }

    private void Window_CustomMouseMove(object sender, MouseEventArgs e)
    {
        if (this.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            // Read the new position against the window
            Point currentPosition = e.GetPosition(this);

            // Calculate the raw pixel movement delta
            double deltaX = currentPosition.X - _clickOffset.X;
            double deltaY = currentPosition.Y - _clickOffset.Y;

            // Apply the delta directly. Since the window moves underneath the mouse, 
            // the next frame naturally self-corrects without looping or jumping.
            this.Left += deltaX;
            this.Top += deltaY;
        }
    }

    private void Window_CustomMouseUp(object sender, MouseButtonEventArgs e)
    {
        this.ReleaseMouseCapture();
    }
}

// UNIQUE CLASS NAME: Safe from clashing with other templates
public class SpeedometerGlowEngine : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return 0.0;
        try
        {
            double currentSpeed = System.Convert.ToDouble(value);
            double targetMarker = System.Convert.ToDouble(parameter);
            
            double proximity = Math.Abs(currentSpeed - targetMarker);
            // Divisor is 60. Adjacent numbers (30 MPH away) will now glow at exactly 50% brightness!
            return Math.Max(0, 1.0 - (proximity / 60.0));
        }
        catch { return 0.0; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}