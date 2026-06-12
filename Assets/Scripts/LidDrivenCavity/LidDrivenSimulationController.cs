using UnityEngine;
using Unity.Mathematics;
using TMPro;
using UnityEngine.InputSystem;

public class LidDrivenSimulationController : Singleton<LidDrivenSimulationController>
{
    [Header("Domain Settings")]
    [SerializeField] private GameObject domain;
    [SerializeField] private float cellSize = 0.01f; // [Range(0.004f, 0.025f)]

    [Header("Simulation Parameters")]
    [SerializeField] private ComputeShader lbmComputeShader;
    [SerializeField] private float lidSpeed;
    [SerializeField] private float tau = 0.6f;

    private LidDrivenSolver _lbmSolver;
    public LidDrivenSolver LBMSolver => _lbmSolver;
    public float LidSpeed => lidSpeed;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        float lx = domain.transform.localScale.x;
        float ly = domain.transform.localScale.y;
        float lz = domain.transform.localScale.z;

        uint nx = (uint)(math.round(lx / cellSize));
        uint ny = (uint)(math.round(ly / cellSize));
        uint nz = (uint)(math.round(lz / cellSize));

        _lbmSolver = new LidDrivenSolver(lbmComputeShader, lidSpeed, cellSize, tau, nx, ny, nz);

        Debug.Log($"Reynolds number: {3f * lidSpeed / ((tau - 0.5) * cellSize):F2}");
    }

    // Update is called once per frame
    void Update()
    {
        _lbmSolver?.Step();

        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            Debug.Log("Exporting data to CSV...");
            _lbmSolver.RequestVelocityDataAndSaveToCSV();
        }

    }

    // Called when the MonoBehaviour will be destroyed
    void OnDestroy()
    {
        _lbmSolver?.Dispose();
    }
}
