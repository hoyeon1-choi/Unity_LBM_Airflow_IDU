using System;
using System.Collections.Generic;

public enum SignalValueType
{
    Real,
    Integer,
    Boolean,
    String
}

public enum SignalDirection
{
    Input,
    Output,
    Parameter,
    Local
}

[Serializable]
public struct CoSimSignalKey : IEquatable<CoSimSignalKey>
{
    public string modelId;
    public string variableName;

    public CoSimSignalKey(string modelId, string variableName)
    {
        this.modelId = modelId ?? string.Empty;
        this.variableName = variableName ?? string.Empty;
    }

    public bool Equals(CoSimSignalKey other)
    {
        return string.Equals(modelId, other.modelId, StringComparison.Ordinal) &&
               string.Equals(variableName, other.variableName, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is CoSimSignalKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (modelId != null ? modelId.GetHashCode() : 0);
            hash = hash * 31 + (variableName != null ? variableName.GetHashCode() : 0);
            return hash;
        }
    }

    public override string ToString()
    {
        return $"{modelId}.{variableName}";
    }
}

[Serializable]
public struct CoSimSignalValue
{
    public SignalValueType type;
    public double realValue;
    public int intValue;
    public bool boolValue;
    public string stringValue;
    public double simTimeSeconds;

    public static CoSimSignalValue FromReal(double value, double simTimeSeconds)
    {
        return new CoSimSignalValue
        {
            type = SignalValueType.Real,
            realValue = value,
            simTimeSeconds = simTimeSeconds
        };
    }

    public static CoSimSignalValue FromInteger(int value, double simTimeSeconds)
    {
        return new CoSimSignalValue
        {
            type = SignalValueType.Integer,
            intValue = value,
            simTimeSeconds = simTimeSeconds
        };
    }

    public static CoSimSignalValue FromBoolean(bool value, double simTimeSeconds)
    {
        return new CoSimSignalValue
        {
            type = SignalValueType.Boolean,
            boolValue = value,
            simTimeSeconds = simTimeSeconds
        };
    }

    public static CoSimSignalValue FromString(string value, double simTimeSeconds)
    {
        return new CoSimSignalValue
        {
            type = SignalValueType.String,
            stringValue = value ?? string.Empty,
            simTimeSeconds = simTimeSeconds
        };
    }

    public bool TryGetReal(out double value)
    {
        switch (type)
        {
            case SignalValueType.Real:
                value = realValue;
                return true;
            case SignalValueType.Integer:
                value = intValue;
                return true;
            case SignalValueType.Boolean:
                value = boolValue ? 1.0 : 0.0;
                return true;
            default:
                value = 0.0;
                return false;
        }
    }

    public CoSimSignalValue WithRealValue(double value)
    {
        CoSimSignalValue copy = this;
        copy.type = SignalValueType.Real;
        copy.realValue = value;
        return copy;
    }

    public override string ToString()
    {
        switch (type)
        {
            case SignalValueType.Real:
                return realValue.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
            case SignalValueType.Integer:
                return intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case SignalValueType.Boolean:
                return boolValue ? "true" : "false";
            case SignalValueType.String:
                return stringValue ?? string.Empty;
            default:
                return string.Empty;
        }
    }
}

public interface ICoSimSignalProvider
{
    bool TryGetSignal(CoSimSignalKey key, out CoSimSignalValue value);
}

public interface ICoSimSignalReceiver
{
    bool TrySetSignal(CoSimSignalKey key, CoSimSignalValue value);
}

public interface ICoSimulationModel
{
    string ModelId { get; }
    bool IsInitialized { get; }
    void Initialize(double startTime, double stopTime, double stepSize);
    void SetInput(string variableName, CoSimSignalValue value);
    CoSimSignalValue GetOutput(string variableName);
    void DoStep(double currentTime, double stepSize);
    void TerminateOrDispose();
}

[Serializable]
public class CoSimConnection
{
    public bool enabled = true;
    public string sourceModelId = "airflow";
    public string sourceVariableName = "T_sensor";
    public string targetModelId = "Simple_CFMU";
    public string targetVariableName = "T_sensor";
    public double scale = 1.0;
    public double offset = 0.0;
    public bool useClampMin = false;
    public double clampMin = 0.0;
    public bool useClampMax = false;
    public double clampMax = 0.0;
    public string description = string.Empty;

    public CoSimSignalKey SourceKey => new CoSimSignalKey(sourceModelId, sourceVariableName);
    public CoSimSignalKey TargetKey => new CoSimSignalKey(targetModelId, targetVariableName);

    public CoSimSignalValue Transform(CoSimSignalValue source)
    {
        double real;
        if (!source.TryGetReal(out real))
            return source;

        double transformed = real * scale + offset;
        if (useClampMin && transformed < clampMin)
            transformed = clampMin;
        if (useClampMax && transformed > clampMax)
            transformed = clampMax;

        return source.WithRealValue(transformed);
    }
}

public class CoSimSignalBus
{
    private readonly Dictionary<CoSimSignalKey, CoSimSignalValue> signals =
        new Dictionary<CoSimSignalKey, CoSimSignalValue>();

    public void Clear()
    {
        signals.Clear();
    }

    public void Publish(CoSimSignalKey key, CoSimSignalValue value)
    {
        signals[key] = value;
    }

    public bool TryGet(CoSimSignalKey key, out CoSimSignalValue value)
    {
        return signals.TryGetValue(key, out value);
    }

    public bool TryTransfer(CoSimConnection connection, out CoSimSignalValue value, out string status)
    {
        value = default;

        if (connection == null)
        {
            status = "Connection is null.";
            return false;
        }

        if (!connection.enabled)
        {
            status = $"Connection disabled: {connection.SourceKey} -> {connection.TargetKey}";
            return false;
        }

        CoSimSignalValue sourceValue;
        if (!TryGet(connection.SourceKey, out sourceValue))
        {
            status = $"Source missing: {connection.SourceKey}";
            return false;
        }

        value = connection.Transform(sourceValue);
        Publish(connection.TargetKey, value);
        status = $"Transferred {connection.SourceKey} -> {connection.TargetKey} = {value}";
        return true;
    }
}
