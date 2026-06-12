using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class LidDrivenVisualizer : MonoBehaviour
{
    public Transform volumeTransform;

    private Material _sliceMaterial;
    private Matrix4x4 _unitizeMatrix; // Cube local space to unit UVW space
    private float _lidSpeed;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _sliceMaterial = GetComponent<Renderer>().material;

        LidDrivenSimulationController simulationController = LidDrivenSimulationController.Instance;
        
        if (simulationController.LBMSolver.VelocityTexture != null)
        {
            // Set the texture from the LBMSolver
            _sliceMaterial.SetTexture("_VolumeTex", simulationController.LBMSolver.VelocityTexture);
        }
        else
        {
            Debug.LogError("SimulationController or LBMSolver is not initialized.");
        }

        _lidSpeed = simulationController.LidSpeed;
        _sliceMaterial.SetFloat("_LidSpeed", _lidSpeed);

        _unitizeMatrix = Matrix4x4.TRS(Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.999999f);
    }

    // Update is called once per frame
    void Update()
    {
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