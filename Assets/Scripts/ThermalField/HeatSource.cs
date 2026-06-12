using System;
using Unity.Mathematics;
using UnityEngine;


[Serializable]
public enum HeatSourceType
{
    Square,
    Cylinder,
}


public class HeatSource : MonoBehaviour
{
    [SerializeField] private float _temperature = 0.0f;
    [SerializeField] private HeatSourceType _type;

    private uint3 _minIdx;
    private uint3 _maxIdx;
    private float3 _center;
    private float _radius;
    //private Renderer _sourceRenderer;
    private Collider _sourceCollider;

    public float Temperature => _temperature;
    public HeatSourceType Type => _type;
    public uint3 MinIdx => _minIdx;
    public uint3 MaxIdx => _maxIdx;
    public float3 Center => _center;
    public float Radius => _radius;

    // public Renderer SourceRenderer => _sourceRenderer;
    public Collider SourceCollider => _sourceCollider;

    private void Awake()
    {
        // _sourceRenderer = GetComponent<Renderer>();
        _sourceCollider = GetComponent<Collider>();
    }

    public void CalculateBounds(float cellSize, Vector3 domainMinCorner, uint nx, uint ny, uint nz)
    {
        // Bounds worldBounds = _sourceRenderer.bounds;
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

        _center = new uint3(0, 0, 0);
        _radius = 0.0f;
    }

    public void CalculateBoundsForCylinder(float cellSize, Vector3 domainMinCorner, uint nx, uint ny, uint nz)
    {
        Vector3 centerRelative = transform.position - domainMinCorner;

        _center = new uint3(
            (uint)Mathf.Max(0, Mathf.FloorToInt(centerRelative.x / cellSize)),
            (uint)Mathf.Max(0, Mathf.FloorToInt(centerRelative.y / cellSize)),
            (uint)Mathf.Max(0, Mathf.FloorToInt(centerRelative.z / cellSize)));

        _radius = 0.5f * transform.localScale.z / cellSize; // Assuming the cylinder is aligned with the z-axisq

        _minIdx = _maxIdx = new uint3(0, 0, 0);
    }

    private void OnDrawGizmos()
    {
        // if (_sourceRenderer == null)
        // {
        //     _sourceRenderer = GetComponent<Renderer>();
        //     if (_sourceRenderer == null)
        //     {
        //         Debug.Log("Couldn't get the mesh renderer.");
        //         return;
        //     }
        // }
        //
        // Bounds worldBounds = _sourceRenderer.bounds;
        // Debug.Log($"Gizmo for '{gameObject.name}': Bounds Size = {worldBounds.size}, Center = {worldBounds.center}");

        if (_sourceCollider == null)
        {
            _sourceCollider = GetComponent<Collider>();
            if (_sourceCollider == null)
            {
                Debug.Log("Couldn't get any collider.");
                return;
            }
        }

        Bounds worldBounds = _sourceCollider.bounds;
        Gizmos.color = Color.yellow;

        Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);
    }
}
