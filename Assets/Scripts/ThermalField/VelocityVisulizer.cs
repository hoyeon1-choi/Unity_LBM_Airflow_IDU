using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class VelocityVisualizer : MonoBehaviour
{
    public Transform volumeTransform;

    [Header("Display Range (m/s)")]
    [SerializeField] private float speedMin = 0.0f;
    [SerializeField] private float speedMax = 2.0f;

    [Header("Auto Scale")]
    [SerializeField] private bool useSimulationMaxSpeed = true;
    [SerializeField] private float autoScaleMultiplier = 1.0f;

    [Header("Update Rate")]
    [Tooltip("Real-time interval for updating contour slice material uniforms.")]
    [Min(0.05f)]
    [SerializeField] private float updateIntervalSeconds = 1.0f;

    private Material _sliceMaterial;
    private Matrix4x4 _unitizeMatrix;
    private bool _initialized;
    private float _nextUpdateRealtime;

    private float _currentDisplayMin = 0.0f;
    private float _currentDisplayMax = 1.0f;

    public float CurrentDisplayMin => _currentDisplayMin;
    public float CurrentDisplayMax => _currentDisplayMax;

    private IEnumerator Start()
    {
        _sliceMaterial = GetComponent<Renderer>().material;
        _unitizeMatrix = Matrix4x4.TRS(Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.999999f);

        yield return new WaitUntil(() =>
            SimulationController.Instance != null &&
            SimulationController.Instance.LBMSolver != null &&
            SimulationController.Instance.LBMSolver.VelocityTexture != null
        );

        var sc = SimulationController.Instance;
        _sliceMaterial.SetTexture("_VolumeTex", sc.LBMSolver.VelocityTexture);

        ApplyPhysicalScale(sc);
        UpdateWorldToVolumeMatrix();
        _nextUpdateRealtime = Time.unscaledTime + Mathf.Max(updateIntervalSeconds, 0.05f);

        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized) return;
        if (_sliceMaterial == null || volumeTransform == null) return;
        if (Time.unscaledTime < _nextUpdateRealtime) return;

        _nextUpdateRealtime = Time.unscaledTime + Mathf.Max(updateIntervalSeconds, 0.05f);
        UpdateWorldToVolumeMatrix();

        var sc = SimulationController.Instance;
        if (sc != null)
        {
            ApplyPhysicalScale(sc);
        }
    }

    private void UpdateWorldToVolumeMatrix()
    {
        Matrix4x4 worldToVolume = _unitizeMatrix * volumeTransform.worldToLocalMatrix;
        _sliceMaterial.SetMatrix("_WorldToVolume", worldToVolume);
    }

    private void ApplyPhysicalScale(SimulationController sc)
    {
        float displayMax = speedMax;

        if (useSimulationMaxSpeed)
        {
            displayMax = Mathf.Max(sc.MaxWindSpeedPhys * autoScaleMultiplier, 1e-6f);
        }

        _currentDisplayMin = speedMin;
        _currentDisplayMax = Mathf.Max(displayMax, speedMin + 1e-6f);

        _sliceMaterial.SetFloat("_VelocityLatToPhys", sc.LatticeSpeedToPhysicalScale);
        _sliceMaterial.SetFloat("_SpeedMin", _currentDisplayMin);
        _sliceMaterial.SetFloat("_SpeedMax", _currentDisplayMax);
    }
}
