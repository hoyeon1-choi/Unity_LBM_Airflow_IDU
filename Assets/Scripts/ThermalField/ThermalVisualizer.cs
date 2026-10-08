using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class ThermalVisualizer : MonoBehaviour
{
    public Transform volumeTransform;

    [Header("Display Range (degC)")]
    [SerializeField] private bool useSimulationTemperatureRange = true;
    [SerializeField] private float tempMinDegC = 0.0f;
    [SerializeField] private float tempMaxDegC = 40.0f;

    [Header("Update Rate")]
    [Tooltip("Real-time interval for updating contour slice material uniforms.")]
    [Min(0.05f)]
    [SerializeField] private float updateIntervalSeconds = 1.0f;

    private Material _sliceMaterial;
    private RenderTexture _boundThermalTexture;
    private Matrix4x4 _unitizeMatrix;
    private bool _initialized;
    private float _nextUpdateRealtime;

    private float _currentTempMinDegC = 0.0f;
    private float _currentTempMaxDegC = 40.0f;
    private bool _useExternalDisplayRange;
    private float _externalTempMinDegC;
    private float _externalTempMaxDegC = 40.0f;

    public float CurrentTempMinDegC => _currentTempMinDegC;
    public float CurrentTempMaxDegC => _currentTempMaxDegC;

    public void SetDisplayRangeOverride(bool enabled, float minimumDegC, float maximumDegC)
    {
        _useExternalDisplayRange = enabled;
        _externalTempMinDegC = minimumDegC;
        _externalTempMaxDegC = Mathf.Max(maximumDegC, minimumDegC + 0.01f);

        SimulationController controller = SimulationController.Instance;
        if (_sliceMaterial != null && controller != null)
            ApplyTemperatureScale(controller);
    }

    private IEnumerator Start()
    {
        _sliceMaterial = GetComponent<Renderer>().material;
        _unitizeMatrix = Matrix4x4.TRS(Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.999999f);

        yield return new WaitUntil(() =>
            SimulationController.Instance != null &&
            SimulationController.Instance.LBMSolver != null &&
            SimulationController.Instance.LBMSolver.ThermalTexture != null
        );

        var sc = SimulationController.Instance;
        BindCurrentThermalTexture(sc);

        ApplyTemperatureScale(sc);
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
            BindCurrentThermalTexture(sc);
            ApplyTemperatureScale(sc);
        }
    }

    private void BindCurrentThermalTexture(SimulationController sc)
    {
        RenderTexture currentTexture = sc != null && sc.LBMSolver != null
            ? sc.LBMSolver.ThermalTexture
            : null;
        if (currentTexture == null || currentTexture == _boundThermalTexture)
            return;

        _boundThermalTexture = currentTexture;
        _sliceMaterial.SetTexture("_VolumeTex", _boundThermalTexture);
    }

    private void UpdateWorldToVolumeMatrix()
    {
        Matrix4x4 worldToVolume = _unitizeMatrix * volumeTransform.worldToLocalMatrix;
        _sliceMaterial.SetMatrix("_WorldToVolume", worldToVolume);
    }

    private void ApplyTemperatureScale(SimulationController sc)
    {
        float dataMinDegC = sc.TempPhysMinDegC;
        float dataMaxDegC = Mathf.Max(sc.TempPhysMaxDegC, dataMinDegC + 0.01f);
        float minDegC = _useExternalDisplayRange
            ? _externalTempMinDegC
            : useSimulationTemperatureRange ? sc.TempPhysMinDegC : tempMinDegC;
        float maxDegC = _useExternalDisplayRange
            ? _externalTempMaxDegC
            : useSimulationTemperatureRange ? sc.TempPhysMaxDegC : tempMaxDegC;

        if (maxDegC <= minDegC)
            maxDegC = minDegC + 0.01f;

        _currentTempMinDegC = minDegC;
        _currentTempMaxDegC = maxDegC;

        _sliceMaterial.SetFloat("_DataTempMinDegC", dataMinDegC);
        _sliceMaterial.SetFloat("_DataTempMaxDegC", dataMaxDegC);
        _sliceMaterial.SetFloat("_TempMinDegC", _currentTempMinDegC);
        _sliceMaterial.SetFloat("_TempMaxDegC", _currentTempMaxDegC);
    }
}
