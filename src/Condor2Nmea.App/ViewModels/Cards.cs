using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace Condor2Nmea.App.ViewModels;

public enum Health { Off, Waiting, Good, Error }

public static class HealthBrushes
{
    public static Brush For(Health h) => (Brush)Application.Current.Resources[h switch
    {
        Health.Good => "StatusGoodBrush",
        Health.Waiting => "StatusWaitBrush",
        Health.Error => "StatusErrorBrush",
        _ => "StatusOffBrush",
    }];
}

/// <summary>Status card of one data source on the dashboard.</summary>
public sealed partial class SourceCard : ObservableObject
{
    public SourceCard(string title, SymbolRegular icon, string hint)
    {
        Title = title;
        Icon = icon;
        Hint = hint;
        _statusBrush = HealthBrushes.For(Health.Off);
    }

    public string Title { get; }
    public SymbolRegular Icon { get; }
    /// <summary>Static explanation what this source is.</summary>
    public string Hint { get; }

    [ObservableProperty] private string _state = "Aus";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _rate = "";
    [ObservableProperty] private Brush _statusBrush;

    public void Set(Health health, string state, string detail, string rate)
    {
        StatusBrush = HealthBrushes.For(health);
        State = state;
        Detail = detail;
        Rate = rate;
    }
}

/// <summary>Status card of one output on the dashboard.</summary>
public sealed partial class OutputCard : ObservableObject
{
    public OutputCard(string name) => Name = name;

    public string Name { get; }
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private string _instruction = "";
    [ObservableProperty] private Brush _statusBrush = HealthBrushes.For(Health.Off);
}

/// <summary>One large value tile on the dashboard.</summary>
public sealed partial class InstrumentTile : ObservableObject
{
    public InstrumentTile(string title, string unit, SymbolRegular icon)
    {
        Title = title;
        Unit = unit;
        Icon = icon;
    }

    public string Title { get; }
    public SymbolRegular Icon { get; }
    [ObservableProperty] private string _unit;
    [ObservableProperty] private string _value = "–";
    [ObservableProperty] private string _source = "";
}

public sealed record TrafficRow(string Cn, string Pilot, string Plane, string Distance, string Bearing,
    string Vertical, string Altitude, string Speed, string Vario, string Status);

public sealed record MonitorRow(string Time, string Direction, string Channel, string Text, Brush Brush);

public sealed record Option<T>(T Value, string Label);
