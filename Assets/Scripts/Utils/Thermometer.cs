using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class Thermometer : MonoBehaviour
{
    [Header("Simulation Settings")]
    [SerializeField] private GameObject Domain;
    [SerializeField] private Transform tempPointsParent;  
    [SerializeField, Range(0.5f, 3f)] private float fixedTime = 1f;

    [Header("Compute Shader")]
    [SerializeField] private ComputeShader computeShader;

    [Header("Data Path")]
    [SerializeField, ReadOnly] private string folderName = "tempData";
    [SerializeField] private string csvName = "temperature_data";

    private uint nx, ny, nz;

    private GameObject[] tempPoints;
    private List<float> times = new();
    private List<float[]> temperatures = new(); // (time, temperatures)

    private float[] tempArray;
    private ComputeBuffer tempBuffer;
    private int tempKernel;
    private int dispatchSizeX, dispatchSizeY, dispatchSizeZ;
    private int frameCount = 0;

    private bool _initialized = false;

    void Start()
    {
        Time.fixedDeltaTime = fixedTime;
        frameCount = 0;
        init();
        _initialized = true;
    }

    void FixedUpdate()
    {
        if (!_initialized) return;
        writeTemperatures();
        frameCount++;
    }

    private void OnDisable() => saveTemperaturesToCsv();

    private void OnDestroy()
    {
        tempBuffer?.Release();
        tempBuffer = null;
    }

    private void init()
    {
        nx = SimulationController.Instance.Nx;
        ny = SimulationController.Instance.Ny;
        nz = SimulationController.Instance.Nz;

        tempPoints = new GameObject[tempPointsParent.childCount];
        for (int i = 0; i < tempPointsParent.childCount; i++)
        {
            tempPoints[i] = tempPointsParent.GetChild(i).gameObject;
        }

        tempBuffer = new ComputeBuffer((int)(nx * ny * nz), sizeof(float));
        temperatures = new List<float[]>(tempPoints.Length);

        tempArray = new float[nx * ny * nz];

        tempKernel = computeShader.FindKernel("temp_copy");
        computeShader.SetBuffer(tempKernel, "tempBuffer", tempBuffer);
        computeShader.SetBuffer(tempKernel, "temperature", SimulationController.Instance.LBMSolver.TemperatureBuffer);

        dispatchSizeX = Mathf.Max(1, Mathf.CeilToInt(nx / 8f));
        dispatchSizeY = Mathf.Max(1, Mathf.CeilToInt(ny / 8f));
        dispatchSizeZ = Mathf.Max(1, Mathf.CeilToInt(nz / 8f));
    }

    private void writeTemperatures()
    {
        times.Add(fixedTime * frameCount);

        dispatchKernel();
        tempBuffer.GetData(tempArray);

        var row = new float[tempPoints.Length];
        for (int i = 0; i < tempPoints.Length; i++)
        {
            int idx = getIdx(tempPoints[i]);
            if (idx < 0 || idx >= tempArray.Length)
            {
                row[i] = float.NaN;
                continue;
            }
            row[i] = tempArray[idx];
        }

        temperatures.Add(row);
    }

    private int getIdx(GameObject point)
    {
        Vector3 localPos = Domain.transform.InverseTransformPoint(point.transform.position);
        int ix = Mathf.Clamp(Mathf.FloorToInt((localPos.x + 0.5f) * nx), 0, (int)nx - 1);
        int iy = Mathf.Clamp(Mathf.FloorToInt((localPos.y + 0.5f) * ny), 0, (int)ny - 1);
        int iz = Mathf.Clamp(Mathf.FloorToInt((localPos.z + 0.5f) * nz), 0, (int)nz - 1);
        return ix + iy * (int)nx + iz * (int)(nx * ny);
    }

    private void saveTemperaturesToCsv()
    {
        string filePath = Path.Combine(folderName, csvName + ".csv");
            
        using StreamWriter writer = new StreamWriter(filePath);
        {
            string header = "Time";
            for (int i = 0; i < tempPoints.Length; i++)
            {
                header += $",{tempPoints[i].name}";
            }
            writer.WriteLine(header);

            for (int t = 0; t < times.Count; t++)
            {
                string dataLine = times[t].ToString("F2");

                var row = temperatures[t];
                for (int i = 0; i < tempPoints.Length; i++)
                {
                    dataLine += $",{row[i]}";
                }

                writer.WriteLine(dataLine);
            }
        }

        Debug.Log($"Temperature data saved to {filePath}");
    }

    private void dispatchKernel()
    {        
        computeShader.Dispatch(tempKernel, dispatchSizeX, dispatchSizeY, dispatchSizeZ);
    }
}
