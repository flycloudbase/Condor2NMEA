using System.Windows.Controls;

namespace Condor2Nmea.App.Pages;

public partial class SetupPage : Page
{
    public SetupPage()
    {
        DataContext = App.Vm;
        InitializeComponent();
    }
}
