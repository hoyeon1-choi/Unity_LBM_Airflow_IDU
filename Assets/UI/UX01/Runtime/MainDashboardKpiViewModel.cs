using System;

public enum MainDashboardKpiState
{
    Unavailable,
    Normal,
    Warning,
    Error,
    Mock
}

public readonly struct MainDashboardKpiValue
{
    public MainDashboardKpiValue(
        bool hasValue,
        double value,
        string unit,
        int decimalPlaces,
        string status,
        MainDashboardKpiState state)
    {
        HasValue = hasValue;
        Value = value;
        Unit = unit ?? string.Empty;
        DecimalPlaces = Math.Max(0, decimalPlaces);
        Status = status ?? string.Empty;
        State = state;
    }

    public bool HasValue { get; }
    public double Value { get; }
    public string Unit { get; }
    public int DecimalPlaces { get; }
    public string Status { get; }
    public MainDashboardKpiState State { get; }

    public static MainDashboardKpiValue Available(
        double value,
        string unit,
        int decimalPlaces,
        string status,
        MainDashboardKpiState state = MainDashboardKpiState.Normal)
    {
        return new MainDashboardKpiValue(true, value, unit, decimalPlaces, status, state);
    }

    public static MainDashboardKpiValue Unavailable(string unit, string status)
    {
        return new MainDashboardKpiValue(
            false,
            0.0,
            unit,
            0,
            status,
            MainDashboardKpiState.Unavailable);
    }
}

public interface IMainDashboardKpiViewModel
{
    MainDashboardKpiValue RoomAverage { get; }
    MainDashboardKpiValue TemperatureDelta { get; }
    MainDashboardKpiValue MaxVelocity { get; }
    MainDashboardKpiValue MassError { get; }
    MainDashboardKpiValue GpuUsage { get; }

    void Refresh();
}

public sealed class MainDashboardMockKpiViewModel : IMainDashboardKpiViewModel
{
    private const string MockStatus = "Mock data";

    public MainDashboardKpiValue RoomAverage { get; private set; }
    public MainDashboardKpiValue TemperatureDelta { get; private set; }
    public MainDashboardKpiValue MaxVelocity { get; private set; }
    public MainDashboardKpiValue MassError { get; private set; }
    public MainDashboardKpiValue GpuUsage { get; private set; }

    public void Refresh()
    {
        RoomAverage = Mock(24.8, "°C", 1);
        TemperatureDelta = Mock(6.2, "°C", 1);
        MaxVelocity = Mock(3.8, "m/s", 1);
        MassError = Mock(0.02, "%", 2);
        GpuUsage = Mock(92.0, "%", 0);
    }

    private static MainDashboardKpiValue Mock(double value, string unit, int decimalPlaces)
    {
        return MainDashboardKpiValue.Available(
            value,
            unit,
            decimalPlaces,
            MockStatus,
            MainDashboardKpiState.Mock);
    }
}
