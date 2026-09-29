using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("Co-Simulation/Monitoring Chart Series")]
public sealed class CoSimulationMonitorSeriesGraphic : MaskableGraphic
{
    [SerializeField, Min(0.25f)] private float lineThickness = 2.0f;

    private IReadOnlyList<Vector2> points;
    private float minTime;
    private float maxTime = 1.0f;
    private float minValue;
    private float maxValue = 1.0f;

    public float LineThickness
    {
        get => lineThickness;
        set
        {
            lineThickness = Mathf.Max(0.25f, value);
            SetVerticesDirty();
        }
    }

    public void SetData(
        IReadOnlyList<Vector2> source,
        float displayMinTime,
        float displayMaxTime,
        float displayMinValue,
        float displayMaxValue)
    {
        points = source;
        minTime = displayMinTime;
        maxTime = displayMaxTime;
        minValue = displayMinValue;
        maxValue = displayMaxValue;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points == null || points.Count < 2)
            return;

        Rect plot = rectTransform.rect;
        Vector2 previous = default;
        bool hasPrevious = false;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 point = points[i];
            if (point.x < minTime)
                continue;

            Vector2 current = new Vector2(
                Mathf.Lerp(plot.xMin, plot.xMax, Mathf.InverseLerp(minTime, maxTime, point.x)),
                Mathf.Lerp(plot.yMin, plot.yMax, Mathf.InverseLerp(minValue, maxValue, point.y)));
            if (hasPrevious)
                DrawLine(vh, previous, current, lineThickness, color);
            previous = current;
            hasPrevious = true;
        }
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
}
