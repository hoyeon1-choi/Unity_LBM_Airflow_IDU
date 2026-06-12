using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class SpatialMeanTemperatureGrapher : MonoBehaviour
{
    [Header("Sampling")]
    [SerializeField] private float sampleIntervalSec = 0.5f;

    [Header("Graph Window")]
    [Tooltip("How many seconds to show in the graph")]
    [SerializeField] private float windowSeconds = 30f;

    [Header("Y Scale")]
    [Tooltip("If true, auto-scale Y based on visible window")]
    [SerializeField] private bool autoScaleY = true;

    [Tooltip("Used when autoScaleY is false")]
    [SerializeField] private float fixedYMin = 0f;

    [Tooltip("Used when autoScaleY is false")]
    [SerializeField] private float fixedYMax = 1f;

    [Header("Overlay Placement")]
    [SerializeField] private Vector2 graphPos = new Vector2(20, 20);
    [SerializeField] private Vector2 graphSize = new Vector2(520, 220);

    [Header("Appearance")]
    [SerializeField] private float lineWidth = 2f;
    [SerializeField] private bool showText = true;
    [SerializeField] private bool showGrid = true;
    [SerializeField] private int gridX = 5;
    [SerializeField] private int gridY = 4;

    private Material _lineMat;
    private bool _running;
    private Coroutine _routine;

    // time series
    private readonly List<float> _t = new();
    private readonly List<float> _y = new();

    private ThermalSolver Solver => SimulationController.Instance?.LBMSolver;

    void OnEnable()
    {
        CreateLineMaterial();
        _routine = StartCoroutine(SampleLoop());
    }

    void OnDisable()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        _running = false;
    }

    private void CreateLineMaterial()
    {
        // Built-in colored line material
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        _lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        // enable alpha blending
        _lineMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _lineMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _lineMat.SetInt("_Cull", (int)CullMode.Off);
        _lineMat.SetInt("_ZWrite", 0);
    }

    private IEnumerator SampleLoop()
    {
        // wait for solver + temperature buffer ready
        yield return new WaitUntil(() =>
            SimulationController.Instance != null &&
            SimulationController.Instance.LBMSolver != null &&
            SimulationController.Instance.LBMSolver.TemperatureBuffer != null
        );

        _running = true;

        while (_running)
        {
            float tNow = Time.time;
            var buffer = Solver.TemperatureBuffer;

            var req = AsyncGPUReadback.Request(buffer);
            while (!req.done) yield return null;

            if (!req.hasError)
            {
                var data = req.GetData<float>();
                double sum = 0.0;
                int n = data.Length;
                for (int i = 0; i < n; ++i) sum += data[i];
                float mean = (n > 0) ? (float)(sum / n) : float.NaN;

                _t.Add(tNow);
                _y.Add(mean);
                TrimOld(tNow);
            }

            float end = Time.time + Mathf.Max(0.01f, sampleIntervalSec);
            while (Time.time < end) yield return null;
        }
    }

    private void TrimOld(float now)
    {
        float tMin = now - Mathf.Max(1f, windowSeconds);
        while (_t.Count > 2 && _t[0] < tMin)
        {
            _t.RemoveAt(0);
            _y.RemoveAt(0);
        }
    }

    void OnGUI()
    {
        if (_t.Count < 2) return;

        // Draw background box using GUI for simple contrast
        var r = new Rect(graphPos.x, graphPos.y, graphSize.x, graphSize.y);
        GUI.Box(r, GUIContent.none);

        if (showText)
        {
            float yMin, yMax, yLast;
            GetVisibleYStats(out yMin, out yMax, out yLast);

            GUI.Label(new Rect(r.x + 8, r.y + 6, r.width - 16, 18),
                $"Mean Temperature (domain)  last={yLast:F4}   min={yMin:F4}   max={yMax:F4}");
        }
    }

    void OnRenderObject()
    {
        if (_t.Count < 2 || _lineMat == null) return;

        float now = _t[_t.Count - 1];
        float tStart = now - Mathf.Max(1f, windowSeconds);

        float yMin, yMax, yLast;
        GetVisibleYStats(out yMin, out yMax, out yLast);

        // graph rect (pixel)
        Rect r = new Rect(graphPos.x, graphPos.y, graphSize.x, graphSize.y);

        // Convert pixel coords to normalized device coords for GL
        // GL expects -1..1 in viewport, but we can use GL.LoadPixelMatrix for easy pixel drawing.
        _lineMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix();

        if (showGrid)
            DrawGrid(r);

        DrawLineSeries(r, tStart, now, yMin, yMax);

        GL.PopMatrix();
    }

    private void GetVisibleYStats(out float yMin, out float yMax, out float yLast)
    {
        yLast = _y[_y.Count - 1];

        if (!autoScaleY)
        {
            yMin = fixedYMin;
            yMax = Mathf.Max(fixedYMax, fixedYMin + 1e-6f);
            return;
        }

        // auto scale based on visible window
        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;

        for (int i = 0; i < _y.Count; ++i)
        {
            float v = _y[i];
            if (float.IsNaN(v) || float.IsInfinity(v)) continue;
            if (v < minV) minV = v;
            if (v > maxV) maxV = v;
        }

        if (!float.IsFinite(minV) || !float.IsFinite(maxV) || Mathf.Abs(maxV - minV) < 1e-6f)
        {
            minV = yLast - 0.01f;
            maxV = yLast + 0.01f;
        }

        // add padding
        float pad = (maxV - minV) * 0.08f;
        yMin = minV - pad;
        yMax = maxV + pad;
    }

    private void DrawGrid(Rect r)
    {
        // Grid lines (thin)
        GL.Begin(GL.LINES);
        GL.Color(new Color(1, 1, 1, 0.15f));

        int gx = Mathf.Max(1, gridX);
        int gy = Mathf.Max(1, gridY);

        // vertical
        for (int i = 1; i < gx; ++i)
        {
            float x = r.x + r.width * (i / (float)gx);
            GL.Vertex3(x, r.y, 0);
            GL.Vertex3(x, r.y + r.height, 0);
        }

        // horizontal
        for (int j = 1; j < gy; ++j)
        {
            float y = r.y + r.height * (j / (float)gy);
            GL.Vertex3(r.x, y, 0);
            GL.Vertex3(r.x + r.width, y, 0);
        }

        GL.End();
    }

    private void DrawLineSeries(Rect r, float tMin, float tMax, float yMin, float yMax)
    {
        float dt = Mathf.Max(1e-6f, tMax - tMin);
        float dy = Mathf.Max(1e-6f, yMax - yMin);

        // draw series
        GL.Begin(GL.LINES);
        GL.Color(new Color(0.2f, 0.9f, 1.0f, 0.95f));

        // emulate line width by drawing multiple offset lines in Y (simple + cheap)
        int w = Mathf.Clamp(Mathf.RoundToInt(lineWidth), 1, 6);
        for (int k = 0; k < w; ++k)
        {
            float yOffset = (k - (w - 1) * 0.5f) * 0.6f;

            for (int i = 1; i < _t.Count; ++i)
            {
                float t0 = _t[i - 1];
                float t1 = _t[i];
                float v0 = _y[i - 1];
                float v1 = _y[i];

                // skip segments outside window quickly
                if (t1 < tMin) continue;
                if (t0 > tMax) break;

                float x0 = r.x + r.width * ((t0 - tMin) / dt);
                float x1 = r.x + r.width * ((t1 - tMin) / dt);

                float yy0 = r.y + r.height * ((v0 - yMin) / dy);
                float yy1 = r.y + r.height * ((v1 - yMin) / dy);

                GL.Vertex3(x0, yy0 + yOffset, 0);
                GL.Vertex3(x1, yy1 + yOffset, 0);
            }
        }

        GL.End();
    }
}
