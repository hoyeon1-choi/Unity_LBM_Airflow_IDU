using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class TemperatureGraphGraphic : MaskableGraphic
{
    [System.Serializable]
    public class Series
    {
        public string name;
        public Color color = Color.white;
        public readonly List<Vector2> points = new();
    }

    [Header("Axis Range")]
    [SerializeField] private float yMin = 0f;
    [SerializeField] private float yMax = 40f;
    [SerializeField] private float minTimeRange = 10f;

    [Header("Padding")]
    [SerializeField] private float leftPadding = 64f;
    [SerializeField] private float rightPadding = 20f;
    [SerializeField] private float topPadding = 20f;
    [SerializeField] private float bottomPadding = 44f;

    [Header("Style")]
    [SerializeField] private float lineThickness = 3f;
    [SerializeField] private float axisThickness = 2f;
    [SerializeField] private float gridThickness = 1f;

    [Header("Grid")]
    [SerializeField] private int xGridCount = 6;
    [SerializeField] private int yGridCount = 4;
    [SerializeField] private Color backgroundColor = new Color(1f, 1f, 1f, 0.96f);
    [SerializeField] private Color axisColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color gridColor = new Color(0.75f, 0.75f, 0.75f, 0.55f);

    [Header("Series Colors")]
    [SerializeField] private Color inletColor = Color.blue;
    [SerializeField] private Color outletColor = Color.red;
    [SerializeField] private Color averageColor = Color.black;

    private readonly Series _inletSeries = new();
    private readonly Series _outletSeries = new();
    private readonly Series _avgSeries = new();

    public float CurrentMaxTime { get; private set; } = 10f;
    public float YMin => yMin;
    public float YMax => yMax;

    protected override void Awake()
    {
        base.Awake();
        _inletSeries.name = "Inlet";
        _inletSeries.color = inletColor;

        _outletSeries.name = "Outlet";
        _outletSeries.color = outletColor;

        _avgSeries.name = "Room Avg";
        _avgSeries.color = averageColor;
    }

    public void ClearAll()
    {
        _inletSeries.points.Clear();
        _outletSeries.points.Clear();
        _avgSeries.points.Clear();
        CurrentMaxTime = minTimeRange;
        SetVerticesDirty();
    }

    public void AddSample(float timeSec, float inletDegC, float outletDegC, float avgDegC)
    {
        _inletSeries.points.Add(new Vector2(timeSec, inletDegC));
        _outletSeries.points.Add(new Vector2(timeSec, outletDegC));
        _avgSeries.points.Add(new Vector2(timeSec, avgDegC));

        CurrentMaxTime = Mathf.Max(minTimeRange, timeSec);
        SetVerticesDirty();
    }

    public Vector2 GetPlotOrigin()
    {
        Rect r = rectTransform.rect;
        return new Vector2(r.xMin + leftPadding, r.yMin + bottomPadding);
    }

    public Vector2 GetPlotSize()
    {
        Rect r = rectTransform.rect;
        return new Vector2(
            Mathf.Max(1f, r.width - leftPadding - rightPadding),
            Mathf.Max(1f, r.height - topPadding - bottomPadding));
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        DrawRect(vh, r, backgroundColor);

        Vector2 origin = GetPlotOrigin();
        Vector2 size = GetPlotSize();

        float x0 = origin.x;
        float y0 = origin.y;
        float x1 = origin.x + size.x;
        float y1 = origin.y + size.y;

        for (int i = 0; i <= yGridCount; i++)
        {
            float t = yGridCount <= 0 ? 0f : i / (float)yGridCount;
            float y = Mathf.Lerp(y0, y1, t);
            DrawLine(vh, new Vector2(x0, y), new Vector2(x1, y), gridThickness, gridColor);
        }

        for (int i = 0; i <= xGridCount; i++)
        {
            float t = xGridCount <= 0 ? 0f : i / (float)xGridCount;
            float x = Mathf.Lerp(x0, x1, t);
            DrawLine(vh, new Vector2(x, y0), new Vector2(x, y1), gridThickness, gridColor);
        }

        DrawLine(vh, new Vector2(x0, y0), new Vector2(x1, y0), axisThickness, axisColor);
        DrawLine(vh, new Vector2(x0, y0), new Vector2(x0, y1), axisThickness, axisColor);

        DrawSeries(vh, _inletSeries);
        DrawSeries(vh, _outletSeries);
        DrawSeries(vh, _avgSeries);
    }

    private void DrawSeries(VertexHelper vh, Series series)
    {
        if (series.points.Count < 2)
            return;

        Vector2 prev = DataToLocal(series.points[0]);

        for (int i = 1; i < series.points.Count; i++)
        {
            Vector2 curr = DataToLocal(series.points[i]);
            DrawLine(vh, prev, curr, lineThickness, series.color);
            prev = curr;
        }
    }

    private Vector2 DataToLocal(Vector2 data)
    {
        Vector2 origin = GetPlotOrigin();
        Vector2 size = GetPlotSize();

        float tx = CurrentMaxTime <= 0f ? 0f : Mathf.Clamp01(data.x / CurrentMaxTime);
        float ty = Mathf.InverseLerp(yMin, yMax, data.y);

        return new Vector2(
            origin.x + tx * size.x,
            origin.y + ty * size.y);
    }

    private void DrawRect(VertexHelper vh, Rect rect, Color c)
    {
        int start = vh.currentVertCount;

        vh.AddVert(new Vector3(rect.xMin, rect.yMin), c, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMin, rect.yMax), c, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMax), c, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMin), c, Vector2.zero);

        vh.AddTriangle(start + 0, start + 1, start + 2);
        vh.AddTriangle(start + 0, start + 2, start + 3);
    }

    private void DrawLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color c)
    {
        Vector2 dir = (b - a).normalized;
        if (dir.sqrMagnitude < 1e-10f)
            return;

        Vector2 n = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);

        int start = vh.currentVertCount;

        vh.AddVert(a - n, c, Vector2.zero);
        vh.AddVert(a + n, c, Vector2.zero);
        vh.AddVert(b + n, c, Vector2.zero);
        vh.AddVert(b - n, c, Vector2.zero);

        vh.AddTriangle(start + 0, start + 1, start + 2);
        vh.AddTriangle(start + 0, start + 2, start + 3);
    }
}
