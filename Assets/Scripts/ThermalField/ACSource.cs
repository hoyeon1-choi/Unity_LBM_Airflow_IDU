using System;
using Unity.Mathematics;
using UnityEngine;

public class ACSource : MonoBehaviour
{
    [SerializeField] private bool _power = true;
    [SerializeField] private float _temperature = 0.0f;
    [SerializeField, Tooltip("Airflow source veloicty [m/s]")] private float3 _windSpeedPhys = new float3(0.2f, 0.0f, 0.0f);

    private float3 _windSpeedLat = new float3(0.0f, 0.0f, 0.0f);
    private uint3 _minIdx;
    private uint3 _maxIdx;
    private Collider _sourceCollider;

    public bool Power => _power;
    public float Temperature => _temperature;
    public float3 WindSpeedPhys => _windSpeedPhys;
    public float3 WindSpeedLat
    {
        get => _windSpeedLat;
        set => _windSpeedLat = value;
    }
    public uint3 MinIdx => _minIdx;
    public uint3 MaxIdx => _maxIdx;
    public Collider SourceCollider => _sourceCollider;

    private void Awake()
    {
        _sourceCollider = GetComponent<Collider>();
    }

    public void CalculateBounds(float cellSize, Vector3 domainMinCorner, uint nx, uint ny, uint nz)
    {
        Bounds worldBounds = _sourceCollider.bounds;
        Vector3 minWorld = worldBounds.min;
        Vector3 maxWorld = worldBounds.max;

        Vector3 minRelative = minWorld - domainMinCorner;
        Vector3 maxRelative = maxWorld - domainMinCorner;

        _minIdx = new uint3(
            (uint)Mathf.Max(0, Mathf.FloorToInt(minRelative.x / cellSize)),
            (uint)Mathf.Max(0, Mathf.FloorToInt(minRelative.y / cellSize)),
            (uint)Mathf.Max(0, Mathf.FloorToInt(minRelative.z / cellSize)));

        _maxIdx = new uint3(
            (uint)Mathf.Min(nx - 1, Mathf.FloorToInt(maxRelative.x / cellSize)),
            (uint)Mathf.Min(ny - 1, Mathf.FloorToInt(maxRelative.y / cellSize)),
            (uint)Mathf.Min(nz - 1, Mathf.FloorToInt(maxRelative.z / cellSize)));
    }

    private void OnDrawGizmos()
    {
        if (_sourceCollider == null)
        {
            _sourceCollider = GetComponent<Collider>();
            if (_sourceCollider == null)
            {
                Debug.Log("Couldn't get the mesh renderer.");
                return;
            }
        }

        Bounds worldBounds = _sourceCollider.bounds;
        // Debug.Log($"Gizmo for '{gameObject.name}': Bounds Size = {worldBounds.size}, Center = {worldBounds.center}");

        Gizmos.color = Color.yellow;

        Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);
    }
}
