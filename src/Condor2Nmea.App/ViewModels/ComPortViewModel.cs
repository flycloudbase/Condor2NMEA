using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condor2Nmea.Runtime.CondorFiles;
using Condor2Nmea.Runtime.VirtualCom;

namespace Condor2Nmea.App.ViewModels;

/// <summary>Virtual COM port management via com0com.</summary>
public sealed partial class ComPortViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ComPortViewModel(MainViewModel main)
    {
        _main = main;
        _comNumber = SafeSuggest();
        _ = RefreshAsync();
    }

    public ObservableCollection<Com0comPair> Pairs { get; } = new();

    [ObservableProperty] private Com0comPair? _selectedPair;
    [ObservableProperty] private int _comNumber;
    [ObservableProperty] private string _installStatus = "";
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _errorText = "";

    /// <summary>A pair exists and the NMEA input reads its internal side.</summary>
    [ObservableProperty] private bool _portReady;
    /// <summary>Name Condor has to use, e.g. "COM9".</summary>
    [ObservableProperty] private string _condorPortName = "COMx";

    public bool HasPairs => Pairs.Count > 0;

    private Com0comManager Manager => new(_main.Settings.Com0com.InstallPath);

    private static int SafeSuggest()
    {
        try { return ComPortRegistry.SuggestFreeComNumber(); }
        catch (Exception) { return 9; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        ErrorText = "";
        var mgr = Manager;
        IsInstalled = mgr.IsInstalled;
        if (!IsInstalled)
        {
            InstallStatus = "com0com ist noch nicht installiert.";
            Pairs.Clear();
            UpdateReady();
            return;
        }
        InstallStatus = $"com0com ist installiert ({mgr.InstallDirectory}).";
        try
        {
            Busy = true;
            var pairs = await mgr.ListPairsAsync();
            Pairs.Clear();
            foreach (var p in pairs)
                Pairs.Add(p);
            SelectedPair = Pairs.FirstOrDefault(p => IsInput(p)) ?? Pairs.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            Busy = false;
            ComNumber = SafeSuggest();
            UpdateReady();
        }
    }

    private bool IsInput(Com0comPair p) =>
        string.Equals(p.AppPort, _main.Settings.Serial.PortName, StringComparison.OrdinalIgnoreCase);

    private void UpdateReady()
    {
        var input = Pairs.FirstOrDefault(IsInput);
        PortReady = input is not null && _main.Settings.Serial.Enabled;
        CondorPortName = input?.CondorPort ?? Pairs.FirstOrDefault()?.CondorPort ?? $"COM{ComNumber}";
        OnPropertyChanged(nameof(HasPairs));
    }

    [RelayCommand]
    private async Task CreatePair()
    {
        ErrorText = "";
        try
        {
            if (ComPortRegistry.GetUsedComNumbers().Contains(ComNumber) &&
                MessageBox.Show($"COM{ComNumber} ist bereits belegt oder reserviert. Trotzdem verwenden?", "Virtueller COM-Port",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            Busy = true;
            var pair = await Manager.CreatePairAsync(ComNumber);
            await UseAsInput(pair);
            _main.MarkPortCreated();
            await RefreshAsync();
            if (CondorInstallation.IsCondorRunning())
                MessageBox.Show($"Der Port {pair.CondorPort} wurde angelegt.\n\nCondor läuft bereits – bitte Condor neu starten, damit der Port in der Liste erscheint.",
                    "Virtueller COM-Port", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            ErrorText = "Die Administrator-Abfrage wurde abgebrochen.";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task RemovePair(Com0comPair? pair)
    {
        pair ??= SelectedPair;
        if (pair is null)
            return;
        if (MessageBox.Show($"Den virtuellen Port {pair.CondorPort} ↔ {pair.AppPort} entfernen?", "Virtueller COM-Port",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        ErrorText = "";
        try
        {
            Busy = true;
            var wasRunning = _main.IsRunning;
            if (wasRunning)
                await _main.StopEngine(); // release the port first
            await Manager.RemovePairAsync(pair.Index);
            await RefreshAsync();
            if (wasRunning)
                _main.StartEngine();
        }
        catch (OperationCanceledException)
        {
            ErrorText = "Die Administrator-Abfrage wurde abgebrochen.";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task UsePair(Com0comPair? pair)
    {
        pair ??= SelectedPair;
        if (pair is not null)
            await UseAsInput(pair);
    }

    private async Task UseAsInput(Com0comPair pair)
    {
        _main.Settings.Serial.PortName = pair.AppPort;
        _main.Settings.Serial.Enabled = true;
        _main.SaveSettings();
        _main.NotifySettingsChanged();
        UpdateReady();
        if (_main.IsRunning)
        {
            await _main.StopEngine();
            _main.StartEngine();
        }
    }

    [RelayCommand]
    private void BrowseCom0com()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "com0com-Ordner (mit setupc.exe) wählen" };
        if (dlg.ShowDialog() == true)
        {
            _main.Settings.Com0com.InstallPath = dlg.FolderName;
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    private void OpenDownload()
    {
        try { Process.Start(new ProcessStartInfo(Com0comManager.DownloadUrl) { UseShellExecute = true }); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
}
