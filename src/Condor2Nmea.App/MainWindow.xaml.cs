using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using Condor2Nmea.App.ViewModels;

namespace Condor2Nmea.App;

public partial class MainWindow : Window
{
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        var vm = (MainViewModel)DataContext;
        ((INotifyCollectionChanged)vm.Monitor).CollectionChanged += (_, e) =>
        {
            // auto-scroll to the newest line
            if (e.Action == NotifyCollectionChangedAction.Add && !vm.MonitorPaused && vm.Monitor.Count > 0)
                MonitorList.ScrollIntoView(vm.Monitor[^1]);
        };
        Loaded += (_, _) =>
        {
            if (vm.Settings.AutoStart)
                vm.StartEngine();
        };
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closing)
            return;
        e.Cancel = true;
        _closing = true;
        await ((MainViewModel)DataContext).ShutdownAsync();
        Close();
    }
}
