using System.Windows.Controls;

namespace Condor2Nmea.App.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        DataContext = App.Vm;
        InitializeComponent();
    }
}
