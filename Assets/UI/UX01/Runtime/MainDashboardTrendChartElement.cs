using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class MainDashboardTrendChartElement : VisualElement
{
    private const int DefaultMaximumSamples = 300;
    private const float DefaultVisibleTimeRangeSeconds = 60.0f;

    private readonly List<Vector2>[] seriesPoints;
    private readonly Color[] seriesColors;
    private readonly int[] seriesAxisIndices;
    private readonly float[] baseMinValues;
    private readonly float[] baseMaxValues;
    private readonly bool expandValueRange;
    private readonly float autoScalePaddingRatio;
    private readonly float[] valuePanOffsets;
    private readonly int maximumSamples;
    private float visibleTimeRangeSeconds;
    private float timePanOffsetSeconds;
    private float latestTime;
    private int horizontalGridDivisionCount = 4;
    private int verticalGridDivisionCount = 6;

    public MainDashboardTrendChartElement(
        Color[] colors,
        float minValue,
        float maxValue,
        bool expandValueRange,
        int maximumSamples = DefaultMaximumSamples,
        float visibleTimeRangeSeconds = DefaultVisibleTimeRangeSeconds,
        float autoScalePaddingRatio = 0.0f)
        : this(
            colors,
            CreateSingleAxisMap(colors?.Length ?? 0),
            new[] { minValue },
            new[] { maxValue },
            expandValueRange,
            maximumSamples,
            visibleTimeRangeSeconds,
            autoScalePaddingRatio)
    {
    }

    public MainDashboardTrendChartElement(
        Color[] colors,
        int[] seriesAxisIndices,
        float[] minValues,
        float[] maxValues,
        bool expandValueRange,
        int maximumSamples = DefaultMaximumSamples,
        float visibleTimeRangeSeconds = DefaultVisibleTimeRangeSeconds,
        float autoScalePaddingRatio = 0.0f)
    {
        seriesColors = colors ?? new Color[0];
        this.maximumSamples = Mathf.Max(2, maximumSamples);
        this.visibleTimeRangeSeconds = Mathf.Max(0.1f, visibleTimeRangeSeconds);
        this.seriesAxisIndices = CreateValidatedAxisMap(
            this.seriesColors.Length,
            seriesAxisIndices,
            minValues?.Length ?? 0);
        baseMinValues = CreateValidatedAxisValues(minValues, 0.0f);
        baseMaxValues = CreateValidatedAxisValues(maxValues, 1.0f, baseMinValues.Length);
        for (int axisIndex = 0; axisIndex < baseMaxValues.Length; axisIndex++)
            baseMaxValues[axisIndex] = Mathf.Max(baseMinValues[axisIndex] + 0.01f, baseMaxValues[axisIndex]);
        valuePanOffsets = new float[baseMinValues.Length];

        seriesPoints = new List<Vector2>[this.seriesColors.Length];
        for (int i = 0; i < seriesPoints.Length; i++)
            seriesPoints[i] = new List<Vector2>(this.maximumSamples);

        this.expandValueRange = expandValueRange;
        this.autoScalePaddingRatio = Mathf.Clamp(autoScalePaddingRatio, 0.0f, 1.0f);
        pickingMode = PickingMode.Ignore;
        AddToClassList("trend-chart-graphic");
        generateVisualContent += DrawChart;
    }

    public float VisibleTimeRangeSeconds => visibleTimeRangeSeconds;
    public float DisplayMinTime => Mathf.Max(0.0f, DisplayMaxTime - visibleTimeRangeSeconds);
    public float DisplayMaxTime => Mathf.Max(visibleTimeRangeSeconds, latestTime - timePanOffsetSeconds);
    public bool CanPanEarlier => timePanOffsetSeconds < GetMaximumTimePanOffset() - 0.001f;
    public bool CanPanLater => timePanOffsetSeconds > 0.001f;

    public void GetDisplayValueRange(out float minValue, out float maxValue)
    {
        GetDisplayValueRange(0, out minValue, out maxValue);
    }

    public void GetDisplayValueRange(int axisIndex, out float minValue, out float maxValue)
    {
        GetValueRange(Mathf.Clamp(axisIndex, 0, baseMinValues.Length - 1), out minValue, out maxValue);
    }

    public void ClearSamples()
    {
        for (int i = 0; i < seriesPoints.Length; i++)
            seriesPoints[i].Clear();

        latestTime = 0.0f;
        timePanOffsetSeconds = 0.0f;
        MarkDirtyRepaint();
    }

    public void SetVisibleTimeRange(float seconds)
    {
        visibleTimeRangeSeconds = Mathf.Max(0.1f, seconds);
        ClampTimePan();
        MarkDirtyRepaint();
    }

    public bool PanTime(float deltaSeconds)
    {
        float previousOffset = timePanOffsetSeconds;
        timePanOffsetSeconds = Mathf.Max(0.0f, timePanOffsetSeconds + deltaSeconds);
        ClampTimePan();
        MarkDirtyRepaint();
        return Mathf.Abs(previousOffset - timePanOffsetSeconds) > 0.001f;
    }

    public void ResetView()
    {
        timePanOffsetSeconds = 0.0f;
        for (int axisIndex = 0; axisIndex < valuePanOffsets.Length; axisIndex++)
            valuePanOffsets[axisIndex] = 0.0f;
        MarkDirtyRepaint();
    }

    public void PanValues(float normalizedDelta)
    {
        if (!float.IsFinite(normalizedDelta) || Mathf.Abs(normalizedDelta) < 0.000001f)
            return;

        for (int axisIndex = 0; axisIndex < valuePanOffsets.Length; axisIndex++)
        {
            GetAutomaticValueRange(axisIndex, out float minimum, out float maximum);
            valuePanOffsets[axisIndex] += (maximum - minimum) * normalizedDelta;
        }
        MarkDirtyRepaint();
    }

    public void SetGridDivisions(int horizontalDivisions, int verticalDivisions)
    {
        horizontalGridDivisionCount = Mathf.Clamp(horizontalDivisions, 2, 20);
        verticalGridDivisionCount = Mathf.Clamp(verticalDivisions, 2, 30);
        MarkDirtyRepaint();
    }

    public void AddSample(float timeSeconds, params float[] values)
    {
        if (values == null)
            return;

        float previousLatestTime = latestTime;
        latestTime = Mathf.Max(latestTime, timeSeconds);
        if (timePanOffsetSeconds > 0.0f && latestTime > previousLatestTime)
            timePanOffsetSeconds += latestTime - previousLatestTime;
        int count = Mathf.Min(values.Length, seriesPoints.Length);
        for (int i = 0; i < count; i++)
        {
            float value = values[i];
            if (float.IsFinite(value))
                seriesPoints[i].Add(new Vector2(timeSeconds, value));

            while (seriesPoints[i].Count > maximumSamples)
                seriesPoints[i].RemoveAt(0);
        }

        ClampTimePan();
        MarkDirtyRepaint();
    }

    public void AddSeriesSample(int seriesIndex, float timeSeconds, float value)
    {
        if (seriesIndex < 0 || seriesIndex >= seriesPoints.Length || !float.IsFinite(value))
            return;

        latestTime = Mathf.Max(latestTime, timeSeconds);
        List<Vector2> points = seriesPoints[seriesIndex];
        points.Add(new Vector2(timeSeconds, value));
        while (points.Count > maximumSamples)
            points.RemoveAt(0);
        ClampTimePan();
        MarkDirtyRepaint();
    }

    private void DrawChart(MeshGenerationContext context)
    {
        Rect plot = contentRect;
        if (plot.width <= 1.0f || plot.height <= 1.0f)
            return;

        Painter2D painter = context.painter2D;
        DrawGrid(painter, plot);
        float minTime = DisplayMinTime;
        float maxTime = DisplayMaxTime;

        for (int seriesIndex = 0; seriesIndex < seriesPoints.Length; seriesIndex++)
        {
            GetValueRange(seriesAxisIndices[seriesIndex], out float minValue, out float maxValue);
            List<Vector2> points = seriesPoints[seriesIndex];
            bool pathStarted = false;
            painter.BeginPath();
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 point = points[i];
                if (point.x < minTime || point.x > maxTime)
                    continue;

                float x = Mathf.Lerp(plot.xMin, plot.xMax, Mathf.InverseLerp(minTime, maxTime, point.x));
                float y = Mathf.Lerp(plot.yMax, plot.yMin, Mathf.InverseLerp(minValue, maxValue, point.y));
                Vector2 position = new Vector2(x, y);
                if (!pathStarted)
                {
                    painter.MoveTo(position);
                    pathStarted = true;
                }
                else
                {
                    painter.LineTo(position);
                }
            }

            if (!pathStarted)
                continue;

            painter.strokeColor = seriesColors[seriesIndex];
            painter.lineWidth = 2.0f;
            painter.Stroke();
        }
    }

    private void DrawGrid(Painter2D painter, Rect plot)
    {
        painter.strokeColor = new Color(0.20f, 0.35f, 0.45f, 0.42f);
        painter.lineWidth = 1.0f;
        for (int i = 0; i <= horizontalGridDivisionCount; i++)
        {
            float t = i / (float)horizontalGridDivisionCount;
            float y = Mathf.Lerp(plot.yMin, plot.yMax, t);
            painter.BeginPath();
            painter.MoveTo(new Vector2(plot.xMin, y));
            painter.LineTo(new Vector2(plot.xMax, y));
            painter.Stroke();
        }

        for (int i = 0; i <= verticalGridDivisionCount; i++)
        {
            float t = i / (float)verticalGridDivisionCount;
            float x = Mathf.Lerp(plot.xMin, plot.xMax, t);
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, plot.yMin));
            painter.LineTo(new Vector2(x, plot.yMax));
            painter.Stroke();
        }
    }

    private void GetValueRange(int axisIndex, out float minValue, out float maxValue)
    {
        GetAutomaticValueRange(axisIndex, out minValue, out maxValue);
        minValue += valuePanOffsets[axisIndex];
        maxValue += valuePanOffsets[axisIndex];
    }

    private void GetAutomaticValueRange(int axisIndex, out float minValue, out float maxValue)
    {
        minValue = baseMinValues[axisIndex];
        maxValue = baseMaxValues[axisIndex];
        if (!expandValueRange)
            return;

        bool dataOnlyAutoScale = autoScalePaddingRatio > 0.0f;
        bool foundValue = false;
        if (dataOnlyAutoScale)
        {
            minValue = float.PositiveInfinity;
            maxValue = float.NegativeInfinity;
        }

        for (int seriesIndex = 0; seriesIndex < seriesPoints.Length; seriesIndex++)
        {
            if (seriesAxisIndices[seriesIndex] != axisIndex)
                continue;

            List<Vector2> points = seriesPoints[seriesIndex];
            for (int i = 0; i < points.Count; i++)
            {
                minValue = Mathf.Min(minValue, points[i].y);
                maxValue = Mathf.Max(maxValue, points[i].y);
                foundValue = true;
            }
        }

        if (dataOnlyAutoScale && !foundValue)
        {
            minValue = baseMinValues[axisIndex];
            maxValue = baseMaxValues[axisIndex];
            return;
        }

        if (dataOnlyAutoScale)
        {
            float rawMinimum = minValue;
            float rawMaximum = maxValue;
            minValue = rawMinimum >= 0.0f
                ? rawMinimum * (1.0f - autoScalePaddingRatio)
                : rawMinimum * (1.0f + autoScalePaddingRatio);
            maxValue = rawMaximum >= 0.0f
                ? rawMaximum * (1.0f + autoScalePaddingRatio)
                : rawMaximum * (1.0f - autoScalePaddingRatio);
        }

        if (maxValue - minValue < 0.01f)
        {
            float center = (minValue + maxValue) * 0.5f;
            minValue = center - 0.005f;
            maxValue = center + 0.005f;
        }
    }

    private void ClampTimePan()
    {
        timePanOffsetSeconds = Mathf.Clamp(timePanOffsetSeconds, 0.0f, GetMaximumTimePanOffset());
    }

    private float GetMaximumTimePanOffset()
    {
        float oldestTime = latestTime;
        bool foundPoint = false;
        for (int seriesIndex = 0; seriesIndex < seriesPoints.Length; seriesIndex++)
        {
            if (seriesPoints[seriesIndex].Count == 0)
                continue;

            oldestTime = Mathf.Min(oldestTime, seriesPoints[seriesIndex][0].x);
            foundPoint = true;
        }

        return foundPoint
            ? Mathf.Max(0.0f, latestTime - oldestTime - visibleTimeRangeSeconds)
            : 0.0f;
    }

    private static int[] CreateSingleAxisMap(int seriesCount)
    {
        return new int[Mathf.Max(0, seriesCount)];
    }

    private static int[] CreateValidatedAxisMap(
        int seriesCount,
        int[] requestedAxisIndices,
        int requestedAxisCount)
    {
        int axisCount = Mathf.Max(1, requestedAxisCount);
        int[] result = new int[Mathf.Max(0, seriesCount)];
        for (int index = 0; index < result.Length; index++)
        {
            int requested = requestedAxisIndices != null && index < requestedAxisIndices.Length
                ? requestedAxisIndices[index]
                : 0;
            result[index] = Mathf.Clamp(requested, 0, axisCount - 1);
        }

        return result;
    }

    private static float[] CreateValidatedAxisValues(
        float[] requestedValues,
        float fallbackValue,
        int requiredLength = 0)
    {
        int length = Mathf.Max(1, requiredLength, requestedValues?.Length ?? 0);
        float[] result = new float[length];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = requestedValues != null && index < requestedValues.Length
                ? requestedValues[index]
                : fallbackValue;
        }

        return result;
    }
}
