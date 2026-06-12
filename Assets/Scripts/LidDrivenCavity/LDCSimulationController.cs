using NUnit.Framework.Interfaces;
using System;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class LDCSimulationController : Singleton<LDCSimulationController>
{
    [Header("Domain Settings")]
    [SerializeField] private GameObject domain;

    [Header("Simulation Parameters")]
    [SerializeField] private ComputeShader lbmComputeShader;
    [SerializeField] private float lid_speed; // m/s
    [Range(0.025f, 0.2f), SerializeField] private float targetWindSpeedLat = 0.1f;
    [SerializeField] private float dxPhys = 0.01f; // meters, cell size
    [SerializeField] private float tau_f = 0.6f;
    [SerializeField] private float tau_T = 0.5f;
    [SerializeField] private float beta = 0.05f; 
    [Range(0.0f, 1.0f), SerializeField] private float T_ref = 0.5f;
    [SerializeField] private float gravity_y = -9.81f; // m/s^2, gravity in y direction

    [Header("Read-Only Values")]
    [SerializeField, ReadOnly] private float dtPhys = 0.0f;
    [SerializeField, ReadOnly] private float gravityLat = 0.0f; // Lattice gravity
    [SerializeField, ReadOnly] private float maxDomainLengthPhys = 0.0f; // meters (L_phys)
    [SerializeField, ReadOnly] private float maxDomainLengthLat = 0f; // # of cells (L_lat)
    [SerializeField, ReadOnly] private float maxWindSpeedPhys = 0.0f; // m/s magnitude (U_phys)
    [SerializeField, ReadOnly] private float maxWindSpeedLat = 0.0f; // Lattice wind speed (U_lat)
    [SerializeField, ReadOnly] private float nuLat = 0f; // Lattice viscosity
    [SerializeField, ReadOnly] private float alphaLat = 0f; // Lattice thermal diffusivity
    [SerializeField, ReadOnly] private float machNumber = 0.0f; // Ma < 0.3 for stability
    [SerializeField, ReadOnly] private float reynoldsNumber = 0.0f;
    [SerializeField, ReadOnly] private float prandtlNumber = 0.0f; // Pr = 0.71 for air
    [SerializeField, ReadOnly] private float RichardsonNumber = 0.0f; // Ri = 0.01/1/5/10 for Benchmark3
    [SerializeField, ReadOnly] private uint nx;
    [SerializeField, ReadOnly] private uint ny;
    [SerializeField, ReadOnly] private uint nz;

    [Header("Log")]
    [SerializeField] private string FileName = "";

    private LDCSolver _lbmSolver;
    public LDCSolver LBMSolver => _lbmSolver;

    public float LidSpeed => lid_speed;

    public Transform DomainRoot => domain != null ? domain.transform : null;
    public float CellSize => dxPhys;
    public uint Nx => nx;
    public uint Ny => ny;
    public uint Nz => nz;

    private float time = 0f, logTimer = 0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        UpdateReadOnlyValues();

        float lx = domain.transform.localScale.x;
        float ly = domain.transform.localScale.y;
        float lz = domain.transform.localScale.z;

        nx = (uint)(math.round(lx / dxPhys));
        ny = (uint)(math.round(ly / dxPhys));
        nz = (uint)(math.round(lz / dxPhys));

        var heatSources = FindObjectsByType<HeatSource>(FindObjectsSortMode.None);
        if (heatSources.Length == 0)
        {
            Debug.Log("No Heat Source");
        }

        _lbmSolver = new LDCSolver(domain, lbmComputeShader, dxPhys, // dxPhys is inserted as cell size in meters
                                   lid_speed, tau_f, tau_T, T_ref, beta, gravityLat,
                                   nx, ny, nz, heatSources);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UpdateReadOnlyValues();
    }
#endif

    // Update is called once per frame
    void Update()
    {
        _lbmSolver?.Step();

        time += Time.deltaTime;
        logTimer += Time.deltaTime;

        if (logTimer > 30f)
        {
            Debug.Log(time + " seconds elapsed.");
            logTimer -= 30f;
        }

        // export data to CSV
        // if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        if ((Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
            time > 300f)
        {
            Debug.Log("Exporting data to CSV...");
            _lbmSolver.RequestVelocityDataAndSaveToCSV(FileName);
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif
        }

        // screenshot
        // if ((Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
        //     time > 75f)
        // {
        //     ScreenCapture.CaptureScreenshot("Results/"+FileName+".png");
        //     Debug.Log("Screenshot saved to " + FileName + ".png");
        //     #if UNITY_EDITOR
        //     UnityEditor.EditorApplication.isPlaying = false;
        //     #endif
        // }
    }

    // Called when the MonoBehaviour will be destroyed
    void OnDestroy()
    {
        _lbmSolver?.Dispose();
    }

    private void UpdateReadOnlyValues()
    {
        if (domain == null) return;

        float lx = domain.transform.localScale.x;
        float ly = domain.transform.localScale.y;
        float lz = domain.transform.localScale.z;
        maxDomainLengthPhys = math.max(math.max(lx, ly), lz);

        nx = (uint)math.round(lx / dxPhys);
        ny = (uint)math.round(ly / dxPhys);
        nz = (uint)math.round(lz / dxPhys);
        maxDomainLengthLat = math.max(math.max(nx, ny), nz);

        var acSources = FindObjectsByType<ACSource>(FindObjectsSortMode.None);

        maxWindSpeedPhys = lid_speed;
        dtPhys = (targetWindSpeedLat * dxPhys) / maxWindSpeedPhys;
        gravityLat = gravity_y * dtPhys * dtPhys / dxPhys; // Lattice gravity

        nuLat = (tau_f - 0.5f) / 3.0f; // c_s^2 = 1/3 in D3Q19
        maxWindSpeedLat = targetWindSpeedLat;
        reynoldsNumber = maxWindSpeedLat * maxDomainLengthLat / nuLat;
        machNumber = maxWindSpeedLat * math.sqrt(3.0f);

        // alphaLat = (tau_T - 0.5f) / 4.0f; // c_s^2 = 1/4 in D3Q7
        alphaLat = (tau_T - 0.5f) / 3.0f; // c_s^2 = 1/4 in D3Q7
        prandtlNumber = nuLat / alphaLat;

        RichardsonNumber = (math.abs(gravity_y) * beta * maxDomainLengthPhys) / (lid_speed * lid_speed);
    }
}
