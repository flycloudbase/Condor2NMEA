using System.Windows;
using System.Windows.Controls;

namespace Condor2Nmea.App.Pages;

public partial class MonitorPage : Page
{
    public MonitorPage()
    {
        DataContext = App.Vm;
        InitializeComponent();
        Loaded += (_, _) => App.Vm.MonitorAppended += ScrollToEnd;
        Unloaded += (_, _) => App.Vm.MonitorAppended -= ScrollToEnd;
    }

    private void ScrollToEnd()
    {
        if (!App.Vm.MonitorPaused && App.Vm.Monitor.Count > 0)
            MonitorList.ScrollIntoView(App.Vm.Monitor[^1]);
    }
}
