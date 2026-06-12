using System.Collections;
using UnityEngine;

public enum VISUALIZATION_TYPE
{
    VELOCITY,
    THERMAL
}

[RequireComponent(typeof(Renderer))]
public class QuadVisualizer : MonoBehaviour
{
    public Transform volumeTransform;
    public VISUALIZATION_TYPE visType = VISUALIZATION_TYPE.VELOCITY;

    private Material _sliceMaterial;
    private Matrix4x4 _unitizeMatrix; // Cube local space to unit UVW space
    private bool _initialized = false;

    // void Start()
    private IEnumerator Start()
    {
        _sliceMaterial = GetComponent<Renderer>().material;
        // LDCSimulationController simulationController = LDCSimulationController.Instance;
        // ThermalSimulationController simulationController = ThermalSimulationController.Instance;
        SimulationController simulationController = SimulationController.Instance;

        yield return new WaitUntil(() => simulationController.LBMSolver != null); // Wait until LBMSolver is initialized

        if (visType == VISUALIZATION_TYPE.VELOCITY)
        {
            if (simulationController.LBMSolver.VelocityTexture != null)
                _sliceMaterial.SetTexture("_VolumeTex", simulationController.LBMSolver.VelocityTexture);
            else Debug.LogError("SimulationController or LBMSolver is not initialized.");

            // _sliceMaterial.SetFloat("_LidSpeed", simulationController.lidSpeed);
            _sliceMaterial.SetFloat("_LidSpeed", simulationController.MaxWindSpeedPhys);
        }
        else if (visType == VISUALIZATION_TYPE.THERMAL)
        {
            if (simulationController.LBMSolver.ThermalTexture != null)
                _sliceMaterial.SetTexture("_VolumeTex", simulationController.LBMSolver.ThermalTexture);
            else Debug.LogError("SimulationController or LBMSolver is not initialized.");
        }

        _unitizeMatrix = Matrix4x4.TRS(Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.999999f);
        _initialized = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (!_initialized) return;

        if (_sliceMaterial == null || volumeTransform == null)
        {
            Debug.LogError("Visualizer is not properly initialized.");
            return;
        }

        // Calculate the world to volume texture matrix
        Matrix4x4 worldToVolume = _unitizeMatrix * volumeTransform.worldToLocalMatrix;
        _sliceMaterial.SetMatrix("_WorldToVolume", worldToVolume);
    }
}