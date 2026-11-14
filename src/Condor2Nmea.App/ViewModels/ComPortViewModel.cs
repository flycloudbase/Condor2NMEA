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

    public bool HasPairs => Pairs.Count > 0;

    [ObservableProperty] private Com0comPair? _selectedPair;
    [ObservableProperty] private int _comNumber;
    [ObservableProperty] private string _installStatus = "";
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _presentPorts = "";

    public string Com0comPath
    {
        get => _main.Settings.Com0com.InstallPath;
        set
        {
            _main.Settings.Com0com.InstallPath = value;
            OnPropertyChanged();
            _ = RefreshAsync();
        }
    }

    private Com0comManager Manager => new(_main.Settings.Com0com.InstallPath);

    private static int SafeSuggest()
    {
        try { return ComPortRegistry.SuggestFreeComNumber(); }
        catch (Exception) { return 9; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var mgr = Manager;
        IsInstalled = mgr.IsInstalled;
        PresentPorts = string.Join(", ", SafePresentPorts());
        if (!IsInstalled)
        {
            InstallStatus = "com0com ist nicht installiert. Bitte das signierte Setup herunterladen und installieren, " +
                            "dabei alle Optionen zum Anlegen von Beispiel-Ports abwählen.";
            Pairs.Clear();
            OnPropertyChanged(nameof(HasPairs));
            return;
        }
        InstallStatus = $"com0com gefunden: {mgr.InstallDirectory}";
        try
        {
            Busy = true;
            var pairs = await mgr.ListPairsAsync();
            Pairs.Clear();
            foreach (var p in pairs)
                Pairs.Add(p);
            SelectedPair = Pairs.FirstOrDefault(p => string.Equals(p.AppPort, _main.Settings.Serial.PortName, StringComparison.OrdinalIgnoreCase))
                           ?? Pairs.FirstOrDefault();
        }
        catch (Exception ex)
        {
            InstallStatus = $"setupc.exe list fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            Busy = false;
            OnPropertyChanged(nameof(HasPairs));
            ComNumber = SafeSuggest();
        }
    }

    private static IEnumerable<string> SafePresentPorts()
    {
        try { return ComPortRegistry.GetPresentPorts(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    [RelayCommand]
    private async Task CreatePair()
    {
        var used = ComPortRegistry.GetUsedComNumbers();
        if (used.Contains(ComNumber) &&
            MessageBox.Show($"COM{ComNumber} ist bereits belegt oder reserviert. Trotzdem verwenden?", "COM-Port",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            Busy = true;
            var pair = await Manager.CreatePairAsync(ComNumber);
            UsePairCore(pair);
            _main.MarkPortCreated();
            await RefreshAsync();
            var running = CondorInstallation.IsCondorRunning();
            MessageBox.Show(
                $"Virtueller Port angelegt.\n\nIn Condor unter Setup → Options → NMEA output „{pair.CondorPort}“ wählen.\n" +
                $"Condor2NMEA liest die Gegenseite „{pair.AppPort}“." +
                (running ? "\n\nCondor läuft bereits – bitte Condor neu starten, damit der Port erscheint." : ""),
                "COM-Port", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            // UAC prompt cancelled
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "COM-Port anlegen fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task RemovePair()
    {
        if (SelectedPair is not { } pair)
            return;
        if (MessageBox.Show($"Paar {pair} entfernen?", "COM-Port", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
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
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "COM-Port entfernen fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task UsePair()
    {
        if (SelectedPair is not { } pair)
            return;
        UsePairCore(pair);
        if (_main.IsRunning)
        {
            await _main.StopEngine();
            _main.StartEngine();
        }
    }

    private void UsePairCore(Com0comPair pair)
    {
        _main.Settings.Serial.PortName = pair.AppPort;
        _main.Settings.Serial.Enabled = true;
        _main.SaveSettings();
        _main.StatusText = $"NMEA-Eingang: {pair.AppPort} (Condor schreibt auf {pair.CondorPort})";
        OnPropertyChanged(nameof(CurrentInput));
        _main.NotifySettingsChanged();
    }

    public string CurrentInput => _main.Settings.Serial.PortName;

    [RelayCommand]
    private void BrowseCom0com()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "com0com-Ordner (mit setupc.exe) wählen" };
        if (dlg.ShowDialog() == true)
            Com0comPath = dlg.FolderName;
    }

    [RelayCommand]
    private void OpenDownload() =>
        Process.Start(new ProcessStartInfo(Com0comManager.DownloadUrl) { UseShellExecute = true });
}
