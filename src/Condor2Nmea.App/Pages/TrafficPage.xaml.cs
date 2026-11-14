using System.Windows.Controls;

namespace Condor2Nmea.App.Pages;

public partial class TrafficPage : Page
{
    public TrafficPage()
    {
        DataContext = App.Vm;
        InitializeComponent();
    }
}
