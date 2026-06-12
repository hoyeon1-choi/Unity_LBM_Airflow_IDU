using System;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public enum ThermalBoundaryType
{
    Adiabatic,
    Isothermal,
}

public class DeviceObstacles : MonoBehaviour
{
    [Header("Thermal Boundary Conditions")]
    [SerializeField] private ThermalBoundaryType boundaryType;

    [Header("Temperature"),
    Tooltip("Temperature in Celsius for Isothermal boundary condition")]
    [SerializeField] private float temperature = 0.0f;

    private uint3 _minIdx;
    private uint3 _maxIdx;
    private Collider _obstacleCollider;

    public ThermalBoundaryType BoundaryType => boundaryType;
    public float Temperature => temperature;

    public uint3 MinIdx => _minIdx;
    public uint3 MaxIdx => _maxIdx;
    public Collider SourceCollider => _obstacleCollider;

    private void Awake()
    {
        _obstacleCollider = GetComponent<Collider>();
    }

    public void CalculateBounds(float cellSize, Vector3 domainMinCorner, uint nx, uint ny, uint nz)
    {
        Bounds worldBounds = _obstacleCollider.bounds;
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

        // Debug.Log($"'{gameObject.name}' MinIdx({_minIdx.x}, {_minIdx.y}, {_minIdx.z}), MaxIdx({_maxIdx.x}, {_maxIdx.y}, {_maxIdx.z})");
    }

    private void OnDrawGizmos()
    {
        if (_obstacleCollider == null)
        {
            _obstacleCollider = GetComponent<Collider>();
            if (_obstacleCollider == null)
            {
                Debug.Log("Couldn't get any collider.");
                return;
            }
        }

        Bounds worldBounds = _obstacleCollider.bounds;
        Gizmos.color = Color.yellow;

        Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);
    }
}
