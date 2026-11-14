using System.Windows.Controls;

namespace Condor2Nmea.App.Pages;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        DataContext = App.Vm;
        InitializeComponent();
    }
}
