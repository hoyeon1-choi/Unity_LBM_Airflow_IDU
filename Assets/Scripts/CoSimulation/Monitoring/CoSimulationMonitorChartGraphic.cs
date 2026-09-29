using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("Co-Simulation/Monitoring Chart")]
public sealed class CoSimulationMonitorChartGraphic : MaskableGraphic
{
    private sealed class Series
    {
        public readonly List<Vector2> points = new List<Vector2>();
        public Color color;
        public bool visible = true;
        public CoSimulationMonitorSeriesGraphic renderer;
    }

    [Header("History")]
    [SerializeField, Min(10)] private int maximumSamples = 300;
    [SerializeField, Min(10.0f)] private float visibleTimeRangeSeconds = 120.0f;

    [Header("Hierarchy Rendering")]
    [Tooltip("Uses editable Background, Grid, and Series child UI objects.")]
    [SerializeField] private bool useAuthoredHierarchy = true;
    [SerializeField, Min(1)] private int authoredSeriesCount = 1;
    [SerializeField] private Vector4 plotPadding = new Vector4(12.0f, 8.0f, 10.0f, 8.0f);
    [SerializeField, Range(2, 12)] private int horizontalGridLineCount = 5;
    [SerializeField, Range(2, 16)] private int verticalGridLineCount = 7;

    [Header("Default Style")]
    [Tooltip("Default thickness for newly created Series objects. Each Series can be adjusted afterward.")]
    [SerializeField, Min(0.25f)] private float lineThickness = 2.0f;
    [Tooltip("Default thickness for newly created Grid objects. Each Grid Image can be adjusted afterward.")]
    [SerializeField, Min(0.25f)] private float gridThickness = 1.0f;
    [SerializeField] private Color backgroundColor = new Color(0.075f, 0.094f, 0.125f, 1.0f);
    [SerializeField] private Color gridColor = new Color(0.33f, 0.38f, 0.45f, 0.42f);

    private readonly List<Series> series = new List<Series>();
    private readonly Color[] defaultColors =
    {
        new Color(0.20f, 0.78f, 0.94f),
        new Color(1.00f, 0.66f, 0.25f),
        new Color(0.36f, 0.86f, 0.52f),
        new Color(0.80f, 0.48f, 0.96f),
        new Color(1.00f, 0.38f, 0.48f)
    };

    public int AuthoredSeriesCount => authoredSeriesCount;
    public bool HasAuthoredHierarchy =>
        transform.Find("Background") != null &&
        transform.Find("PlotArea/Grid") != null &&
        transform.Find("PlotArea/Series") != null;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    public void ConfigureSeries(int count)
    {
        count = Mathf.Max(1, count);
        authoredSeriesCount = count;
        while (series.Count < count)
        {
            int index = series.Count;
            series.Add(new Series { color = defaultColors[index % defaultColors.Length] });
        }

        while (series.Count > count)
            series.RemoveAt(series.Count - 1);

        if (useAuthoredHierarchy)
            EnsureAuthoredHierarchy(count);
        RefreshRenderers();
        SetVerticesDirty();
    }

    public Color GetSeriesColor(int index)
    {
        if (index < 0 || index >= series.Count)
            return Color.white;
        return series[index].renderer != null ? series[index].renderer.color : series[index].color;
    }

    public void SetSeriesColor(int index, Color value)
    {
        if (index < 0 || index >= series.Count)
            return;

        series[index].color = value;
        if (series[index].renderer != null)
            series[index].renderer.color = value;
        SetVerticesDirty();
    }

    public void SetSeriesVisible(int index, bool visible)
    {
        if (index < 0 || index >= series.Count)
            return;

        series[index].visible = visible;
        if (series[index].renderer != null)
            series[index].renderer.enabled = visible;
        RefreshRenderers();
        SetVerticesDirty();
    }

    public void AddSample(float timeSeconds, params float[] values)
    {
        if (values == null || values.Length == 0)
            return;

        if (series.Count != values.Length)
            ConfigureSeries(values.Length);
        for (int i = 0; i < series.Count; i++)
        {
            float value = values[i];
            if (!float.IsNaN(value) && !float.IsInfinity(value))
                series[i].points.Add(new Vector2(timeSeconds, value));

            while (series[i].points.Count > maximumSamples)
                series[i].points.RemoveAt(0);
        }

        RefreshRenderers();
        SetVerticesDirty();
    }

    public void ClearSamples()
    {
        for (int i = 0; i < series.Count; i++)
            series[i].points.Clear();
        RefreshRenderers();
        SetVerticesDirty();
    }

    public void GetDisplayRanges(out float minTime, out float maxTime, out float minValue, out float maxValue)
    {
        GetRanges(out minTime, out maxTime, out minValue, out maxValue);
    }

    /// <summary>Creates missing Background/Grid/Series objects without replacing existing Inspector edits.</summary>
    public void EnsureAuthoredHierarchy(int seriesCount)
    {
        authoredSeriesCount = Mathf.Max(1, seriesCount);
        RectTransform background = FindOrCreateRect(transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        bool newBackground = background.GetComponent<Image>() == null;
        Image backgroundImage = GetOrAdd<Image>(background.gameObject);
        backgroundImage.raycastTarget = false;
        if (newBackground)
            backgroundImage.color = backgroundColor;
        background.SetAsFirstSibling();

        RectTransform plot = FindOrCreateRect(
            transform,
            "PlotArea",
            Vector2.zero,
            Vector2.one,
            new Vector2(plotPadding.x, plotPadding.z),
            new Vector2(-plotPadding.y, -plotPadding.w));
        GetOrAdd<RectMask2D>(plot.gameObject);

        RectTransform grid = FindOrCreateRect(plot, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        EnsureGridLines(grid, true, horizontalGridLineCount);
        EnsureGridLines(grid, false, verticalGridLineCount);

        RectTransform seriesRoot = FindOrCreateRect(plot, "Series", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        EnsureSeriesRenderers(seriesRoot, authoredSeriesCount);
        seriesRoot.SetAsLastSibling();
        RefreshRenderers();
        SetVerticesDirty();
    }

    private void EnsureGridLines(RectTransform grid, bool horizontal, int count)
    {
        string prefix = horizontal ? "Horizontal" : "Vertical";
        count = Mathf.Max(2, count);
        for (int i = 0; i < count; i++)
        {
            float t = i / (count - 1.0f);
            string name = $"{prefix}_{i + 1:00}";
            Transform existing = grid.Find(name);
            bool created = existing == null;
            RectTransform line = created
                ? FindOrCreateRect(grid, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero)
                : (RectTransform)existing;
            if (created)
            {
                if (horizontal)
                {
                    line.anchorMin = new Vector2(0.0f, t);
                    line.anchorMax = new Vector2(1.0f, t);
                    line.offsetMin = new Vector2(0.0f, -gridThickness * 0.5f);
                    line.offsetMax = new Vector2(0.0f, gridThickness * 0.5f);
                }
                else
                {
                    line.anchorMin = new Vector2(t, 0.0f);
                    line.anchorMax = new Vector2(t, 1.0f);
                    line.offsetMin = new Vector2(-gridThickness * 0.5f, 0.0f);
                    line.offsetMax = new Vector2(gridThickness * 0.5f, 0.0f);
                }
            }

            Image image = GetOrAdd<Image>(line.gameObject);
            image.raycastTarget = false;
            if (created)
                image.color = gridColor;
        }
    }

    private void EnsureSeriesRenderers(RectTransform seriesRoot, int count)
    {
        for (int i = 0; i < count; i++)
        {
            string name = $"Series_{i + 1:00}";
            Transform existing = seriesRoot.Find(name);
            bool created = existing == null;
            RectTransform rect = created
                ? FindOrCreateRect(seriesRoot, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero)
                : (RectTransform)existing;
            CoSimulationMonitorSeriesGraphic renderer = GetOrAdd<CoSimulationMonitorSeriesGraphic>(rect.gameObject);
            renderer.raycastTarget = false;
            if (created)
            {
                renderer.color = defaultColors[i % defaultColors.Length];
                renderer.LineThickness = lineThickness;
            }

            if (i < series.Count)
            {
                series[i].renderer = renderer;
                series[i].color = renderer.color;
                renderer.enabled = series[i].visible;
            }
        }

        for (int i = count; i < seriesRoot.childCount; i++)
        {
            CoSimulationMonitorSeriesGraphic extra = seriesRoot.GetChild(i).GetComponent<CoSimulationMonitorSeriesGraphic>();
            if (extra != null)
                extra.enabled = false;
        }
    }

    private void RefreshRenderers()
    {
        if (!HasAuthoredHierarchy)
            return;

        GetRanges(out float minTime, out float maxTime, out float minValue, out float maxValue);
        for (int i = 0; i < series.Count; i++)
        {
            CoSimulationMonitorSeriesGraphic renderer = series[i].renderer;
            if (renderer == null)
                continue;
            renderer.enabled = series[i].visible;
            renderer.SetData(series[i].points, minTime, maxTime, minValue, maxValue);
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (useAuthoredHierarchy && HasAuthoredHierarchy)
            return;

        Rect bounds = rectTransform.rect;
        DrawRect(vh, bounds, backgroundColor);
        Rect plot = Rect.MinMaxRect(
            bounds.xMin + plotPadding.x,
            bounds.yMin + plotPadding.z,
            bounds.xMax - plotPadding.y,
            bounds.yMax - plotPadding.w);

        for (int i = 0; i < horizontalGridLineCount; i++)
        {
            float t = i / Mathf.Max(1.0f, horizontalGridLineCount - 1.0f);
            DrawLine(vh, new Vector2(plot.xMin, Mathf.Lerp(plot.yMin, plot.yMax, t)),
                new Vector2(plot.xMax, Mathf.Lerp(plot.yMin, plot.yMax, t)), gridThickness, gridColor);
        }
        for (int i = 0; i < verticalGridLineCount; i++)
        {
            float t = i / Mathf.Max(1.0f, verticalGridLineCount - 1.0f);
            DrawLine(vh, new Vector2(Mathf.Lerp(plot.xMin, plot.xMax, t), plot.yMin),
                new Vector2(Mathf.Lerp(plot.xMin, plot.xMax, t), plot.yMax), gridThickness, gridColor);
        }

        GetRanges(out float minTime, out float maxTime, out float minValue, out float maxValue);
        for (int i = 0; i < series.Count; i++)
        {
            if (series[i].visible)
                DrawLegacySeries(vh, series[i], plot, minTime, maxTime, minValue, maxValue);
        }
    }

    private void GetRanges(out float minTime, out float maxTime, out float minValue, out float maxValue)
    {
        maxTime = 0.0f;
        bool found = false;
        for (int s = 0; s < series.Count; s++)
        {
            if (!series[s].visible || series[s].points.Count == 0)
                continue;
            maxTime = Mathf.Max(maxTime, series[s].points[series[s].points.Count - 1].x);
            found = true;
        }

        minTime = Mathf.Max(0.0f, maxTime - visibleTimeRangeSeconds);
        minValue = float.PositiveInfinity;
        maxValue = float.NegativeInfinity;
        for (int s = 0; s < series.Count; s++)
        {
            if (!series[s].visible)
                continue;
            List<Vector2> points = series[s].points;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].x < minTime)
                    continue;
                minValue = Mathf.Min(minValue, points[i].y);
                maxValue = Mathf.Max(maxValue, points[i].y);
            }
        }

        if (!found || float.IsInfinity(minValue) || float.IsInfinity(maxValue))
        {
            minValue = 0.0f;
            maxValue = 1.0f;
        }

        float range = Mathf.Max(0.01f, maxValue - minValue);
        float padding = Mathf.Max(0.25f, range * 0.1f);
        minValue -= padding;
        maxValue += padding;
        if (maxTime - minTime < 1.0e-4f)
            maxTime = minTime + 1.0f;
    }

    private void DrawLegacySeries(VertexHelper vh, Series item, Rect plot, float minTime, float maxTime, float minValue, float maxValue)
    {
        Vector2 previous = default;
        bool hasPrevious = false;
        for (int i = 0; i < item.points.Count; i++)
        {
            Vector2 point = item.points[i];
            if (point.x < minTime)
                continue;
            Vector2 current = new Vector2(
                Mathf.Lerp(plot.xMin, plot.xMax, Mathf.InverseLerp(minTime, maxTime, point.x)),
                Mathf.Lerp(plot.yMin, plot.yMax, Mathf.InverseLerp(minValue, maxValue, point.y)));
            if (hasPrevious)
                DrawLine(vh, previous, current, lineThickness, item.color);
            previous = current;
            hasPrevious = true;
        }
    }

    private static RectTransform FindOrCreateRect(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return (RectTransform)existing;

        GameObject child = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void DrawRect(VertexHelper vh, Rect rect, Color value)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(rect.xMin, rect.yMin), value, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMin, rect.yMax), value, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMax), value, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMin), value, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void DrawLine(VertexHelper vh, Vector2 from, Vector2 to, float thickness, Color value)
    {
        Vector2 direction = to - from;
        if (direction.sqrMagnitude < 1.0e-8f)
            return;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
        int start = vh.currentVertCount;
        vh.AddVert(from - normal, value, Vector2.zero);
        vh.AddVert(from + normal, value, Vector2.zero);
        vh.AddVert(to + normal, value, Vector2.zero);
        vh.AddVert(to - normal, value, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild Chart Hierarchy")]
    public void RebuildChartHierarchy()
    {
        Transform background = transform.Find("Background");
        Transform plotArea = transform.Find("PlotArea");
        if (background != null)
            DestroyImmediate(background.gameObject);
        if (plotArea != null)
            DestroyImmediate(plotArea.gameObject);

        for (int i = 0; i < series.Count; i++)
            series[i].renderer = null;
        EnsureAuthoredHierarchy(Mathf.Max(1, authoredSeriesCount));
        UnityEditor.EditorUtility.SetDirty(gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
#endif
}
