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

    private Material _sliceMaterial;
    private Matrix4x4 _unitizeMatrix;
    private bool _initialized;

    private float _currentTempMinDegC = 0.0f;
    private float _currentTempMaxDegC = 40.0f;

    public float CurrentTempMinDegC => _currentTempMinDegC;
    public float CurrentTempMaxDegC => _currentTempMaxDegC;

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
        _sliceMaterial.SetTexture("_VolumeTex", sc.LBMSolver.ThermalTexture);

        ApplyTemperatureScale(sc);

        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized) return;
        if (_sliceMaterial == null || volumeTransform == null) return;

        Matrix4x4 worldToVolume = _unitizeMatrix * volumeTransform.worldToLocalMatrix;
        _sliceMaterial.SetMatrix("_WorldToVolume", worldToVolume);

        var sc = SimulationController.Instance;
        if (sc != null)
        {
            ApplyTemperatureScale(sc);
        }
    }

    private void ApplyTemperatureScale(SimulationController sc)
    {
        float minDegC = useSimulationTemperatureRange ? sc.TempPhysMinDegC : tempMinDegC;
        float maxDegC = useSimulationTemperatureRange ? sc.TempPhysMaxDegC : tempMaxDegC;

        if (maxDegC <= minDegC)
            maxDegC = minDegC + 0.01f;

        _currentTempMinDegC = minDegC;
        _currentTempMaxDegC = maxDegC;

        _sliceMaterial.SetFloat("_TempMinDegC", _currentTempMinDegC);
        _sliceMaterial.SetFloat("_TempMaxDegC", _currentTempMaxDegC);
    }
}