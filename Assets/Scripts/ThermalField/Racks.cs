using System;
using UnityEngine;
using Unity.Mathematics;

public class Racks : MonoBehaviour
{
    [Header("Power")]
    [SerializeField] private bool power = true;

    [Header("Outer Shell")]
    [SerializeField] private uint shell;

    [Header("Porous parameters")]
    [SerializeField] private Vector3 invK_lat = new Vector3(0.005f, 0.2f, 0.2f); // Inverse permeability (LB units). Smaller X value means easier flow along forward axis.
    [SerializeField] private float betaF = 0.0f; // Forchheimer coefficient (LB units)
    [SerializeField, HideInInspector] private float phi = 0.95f; // 0 < porosity < 1 // not used for now

    [Header("Volumetric heat energy (applied inside racks)")]
    [SerializeField] private float qdot = 0.0f; // Volumetric heat generation (LB units)

    private uint3 _minIdx, _maxIdx;
    private Collider _racksCollider;

    public bool Power => power;
    public uint Shell => shell;
    public Vector3 InvKLat => invK_lat;
    public float BetaF => betaF;
    public float Phi => phi;
    public float Qdot => qdot;

    public uint3 MinIdx => _minIdx;
    public uint3 MaxIdx => _maxIdx;
    public Collider RacksCollider => _racksCollider;

    void Start()
    {
        _racksCollider = GetComponent<Collider>();
    }

    public void CalculateBounds(float cellSize, Vector3 domainMinCorner, uint nx, uint ny, uint nz)
    {
        Bounds worldBounds = _racksCollider.bounds;
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

    // for rack
    public uint GetOpenAxis()
    {
        // 0: x-axis, 1: z-axis

        float dot = Mathf.Abs(Vector3.Dot(gameObject.transform.forward, Vector3.right));

        if (Mathf.Approximately(dot, 0f))
        {
            // Debug.Log($"Rack '{gameObject.name}' open axis: X-axis");
            return 0;
        }
        else
        {
            // Debug.Log($"Rack '{gameObject.name}' open axis: Z-axis");
            return 1;
        }
    }

    private void OnDrawGizmos()
    {
        if (_racksCollider == null)
        {
            _racksCollider = GetComponent<Collider>();
            if (_racksCollider == null)
            {
                Debug.Log("Couldn't get any collider.");
                return;
            }
        }

        Bounds worldBounds = _racksCollider.bounds;
        Gizmos.color = Color.cyan;

        Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);
    }
}