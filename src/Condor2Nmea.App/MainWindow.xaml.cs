using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Condor2Nmea.App.Pages;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Condor2Nmea.App;

public partial class MainWindow : FluentWindow
{
    private bool _closing;

    public MainWindow()
    {
        DataContext = App.Vm;
        InitializeComponent();

        if (App.ScreenshotDirectory is not null)
        {
            // Mica is transparent and cannot be captured with RenderTargetBitmap
            WindowBackdropType = WindowBackdropType.None;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            RootGrid.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "ApplicationBackgroundBrush");
        }
        else
        {
            SystemThemeWatcher.Watch(this);
        }

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var vm = App.Vm;
        if (vm.Settings.AutoStart || App.ScreenshotDirectory is not null || App.ReplayFile is not null)
            vm.StartEngine();

        RootNavigation.Navigate(vm.IsFirstStart ? typeof(SetupPage) : typeof(DashboardPage));

        if (App.ScreenshotDirectory is { } dir)
            await TakeScreenshots(dir);
    }

    /// <summary>CI smoke test: open every page, render it to PNG and exit (exit code 0 = all pages loaded).</summary>
    private async Task TakeScreenshots(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            await Task.Delay(4000); // let the replay deliver data
            var pages = new (Type Type, string Name)[]
            {
                (typeof(DashboardPage), "1-uebersicht"),
                (typeof(SetupPage), "2-einrichtung"),
                (typeof(TrafficPage), "3-verkehr"),
                (typeof(MonitorPage), "4-monitor"),
                (typeof(SettingsPage), "5-einstellungen"),
            };
            foreach (var (type, name) in pages)
            {
                // Navigate returns false when the page is already shown
                if (!RootNavigation.Navigate(type) && RootNavigation.SelectedItem?.TargetPageType != type)
                    throw new InvalidOperationException($"Navigation zu {type.Name} fehlgeschlagen");
                await Task.Delay(1500);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                Save((FrameworkElement)Content, Path.Combine(dir, name + ".png"));
                File.AppendAllText(Path.Combine(dir, "layout.txt"), DescribeLayout(name));
            }
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            Application.Current.Shutdown(1);
        }
    }

    /// <summary>Text description of the visible layout (for checking the UI without seeing it).</summary>
    private string DescribeLayout(string name)
    {
        var root = (FrameworkElement)Content;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"===== {name}  window {ActualWidth:0}x{ActualHeight:0}");
        var lines = 0;
        void Walk(DependencyObject node, int depth)
        {
            if (lines > 260)
                return;
            if (node is FrameworkElement fe && fe.IsVisible && fe.ActualWidth > 0)
            {
                string? label = fe switch
                {
                    System.Windows.Controls.Page => "Page",
                    Card => "Card",
                    CardExpander => "CardExpander",
                    InfoBar ib => ib.IsOpen ? $"InfoBar \"{ib.Title}\"" : null,
                    System.Windows.Controls.Primitives.ButtonBase b => $"Button \"{b.Content}\"",
                    System.Windows.Controls.TextBox tb => $"TextBox \"{Trim(tb.Text)}\"",
                    System.Windows.Controls.ComboBox cb => $"ComboBox \"{Trim(cb.Text)}\"",
                    System.Windows.Controls.TextBlock t when t.TemplatedParent is null or System.Windows.Controls.ContentPresenter => $"Text \"{Trim(t.Text)}\"",
                    System.Windows.Controls.ScrollViewer sv => $"ScrollViewer extent={sv.ExtentHeight:0} viewport={sv.ViewportHeight:0}",
                    Controls.RadarView => "RadarView",
                    Controls.StepBadge sbg => $"StepBadge done={sbg.IsDone}",
                    System.Windows.Controls.DataGrid => "DataGrid",
                    System.Windows.Controls.ListBox lb => $"ListBox items={lb.Items.Count}",
                    _ => null,
                };
                if (label is not null)
                {
                    try
                    {
                        var pos = fe.TransformToAncestor(root).Transform(new Point(0, 0));
                        var clipped = fe is System.Windows.Controls.TextBlock tbk && tbk.DesiredSize.Width - tbk.Margin.Left - tbk.Margin.Right > tbk.ActualWidth + 1 ? "  [GEKÜRZT]" : "";
                        sb.AppendLine($"{new string(' ', Math.Min(depth, 30))}{label}  @({pos.X:0},{pos.Y:0}) {fe.ActualWidth:0}x{fe.ActualHeight:0}{clipped}");
                        lines++;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Walk(VisualTreeHelper.GetChild(node, i), depth + 1);
        }
        Walk(root, 0);
        return sb.ToString();

        static string Trim(string? s) => s is null ? "" : (s.Length > 70 ? s[..70] + "…" : s).Replace("\n", " ");
    }

    private static void Save(FrameworkElement element, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closing || App.ScreenshotDirectory is not null)
            return;
        e.Cancel = true;
        _closing = true;
        await App.Vm.ShutdownAsync();
        Close();
    }
}
