using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Condor2Nmea.App.ViewModels;
using Condor2Nmea.Core.Config;

namespace Condor2Nmea.App;

public partial class App : Application
{
    /// <summary>Shared view model of all pages.</summary>
    public static MainViewModel Vm { get; private set; } = null!;

    /// <summary>--screenshot &lt;dir&gt;: render every page to PNG and exit (used by CI).</summary>
    public static string? ScreenshotDirectory { get; private set; }

    /// <summary>--replay &lt;file&gt;: play a recording instead of the live sources.</summary>
    public static string? ReplayFile { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
        for (var i = 0; i < e.Args.Length - 1; i++)
        {
            if (e.Args[i] == "--screenshot") ScreenshotDirectory = e.Args[i + 1];
            if (e.Args[i] == "--replay") ReplayFile = e.Args[i + 1];
        }

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        base.OnStartup(e);

        Vm = new MainViewModel(ReplayFile, isolated: ScreenshotDirectory is not null);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        if (ScreenshotDirectory is not null)
        {
            Shutdown(1);
            return;
        }
        MessageBox.Show(e.Exception.Message, "Condor2NMEA – Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void LogCrash(Exception? ex)
    {
        if (ex is null)
            return;
        try
        {
            var dir = ScreenshotDirectory ?? SettingsStore.AppDataDirectory;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch (Exception)
        {
            // nothing else we can do
        }
    }
}
