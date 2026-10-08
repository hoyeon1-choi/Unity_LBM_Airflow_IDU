using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class SimulationSetupUserCase
{
    public string id;
    public string name;
    public string description;
    public string basedOnPreset;
    public float dxPhys;
    public float tauFluidMin;
    public float tauThermalMin;
    public string turbulence;
    public string createdUtc;
    public string modifiedUtc;

    public SimulationSetupUserCase Clone()
    {
        return new SimulationSetupUserCase
        {
            id = id,
            name = name,
            description = description,
            basedOnPreset = basedOnPreset,
            dxPhys = dxPhys,
            tauFluidMin = tauFluidMin,
            tauThermalMin = tauThermalMin,
            turbulence = turbulence,
            createdUtc = createdUtc,
            modifiedUtc = modifiedUtc
        };
    }
}

public interface ISimulationSetupCaseStore
{
    string DisplayLocation { get; }
    List<SimulationSetupUserCase> Load(out string issue);
    bool Save(IReadOnlyList<SimulationSetupUserCase> cases, out string issue);
}

public sealed class JsonSimulationSetupCaseStore : ISimulationSetupCaseStore
{
    [Serializable]
    private sealed class CaseFile
    {
        public int version = 1;
        public List<SimulationSetupUserCase> cases = new List<SimulationSetupUserCase>();
    }

    private const int CurrentVersion = 1;
    private readonly string filePath;

    public JsonSimulationSetupCaseStore()
        : this(Path.Combine(Application.persistentDataPath, "UX02", "cases.json"))
    {
    }

    public JsonSimulationSetupCaseStore(string path)
    {
        filePath = path;
    }

    public string DisplayLocation => filePath;

    public List<SimulationSetupUserCase> Load(out string issue)
    {
        issue = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new List<SimulationSetupUserCase>();

        try
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            CaseFile file = JsonUtility.FromJson<CaseFile>(json);
            if (file == null)
            {
                issue = "Case JSON is empty or invalid; built-in cases remain available.";
                return new List<SimulationSetupUserCase>();
            }

            if (file.version != CurrentVersion)
            {
                issue = $"Unsupported case JSON version {file.version}; expected {CurrentVersion}.";
                return new List<SimulationSetupUserCase>();
            }

            var validCases = new List<SimulationSetupUserCase>();
            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            if (file.cases == null)
                return validCases;

            for (int i = 0; i < file.cases.Count; i++)
            {
                SimulationSetupUserCase candidate = file.cases[i];
                if (!IsValid(candidate) || !usedIds.Add(candidate.id))
                    continue;

                validCases.Add(candidate.Clone());
            }

            if (validCases.Count != file.cases.Count)
                issue = "One or more invalid or duplicate user cases were ignored.";
            return validCases;
        }
        catch (Exception exception)
        {
            issue = $"Unable to read case JSON: {exception.Message}";
            return new List<SimulationSetupUserCase>();
        }
    }

    public bool Save(IReadOnlyList<SimulationSetupUserCase> cases, out string issue)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            issue = "Case JSON path is empty.";
            return false;
        }

        string temporaryPath = filePath + ".tmp";
        string backupPath = filePath + ".bak";
        try
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var file = new CaseFile();
            if (cases != null)
            {
                for (int i = 0; i < cases.Count; i++)
                    file.cases.Add(cases[i].Clone());
            }

            File.WriteAllText(temporaryPath, JsonUtility.ToJson(file, true), Encoding.UTF8);
            if (File.Exists(filePath))
                File.Replace(temporaryPath, filePath, backupPath, true);
            else
                File.Move(temporaryPath, filePath);

            issue = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            issue = $"Unable to save case JSON: {exception.Message}";
            TryDeleteTemporaryFile(temporaryPath);
            return false;
        }
    }

    private static bool IsValid(SimulationSetupUserCase candidate)
    {
        return candidate != null &&
               !string.IsNullOrWhiteSpace(candidate.id) &&
               !string.IsNullOrWhiteSpace(candidate.name) &&
               candidate.dxPhys > 0.0f &&
               candidate.tauFluidMin > 0.5000f &&
               candidate.tauThermalMin > 0.5000f &&
               !float.IsNaN(candidate.dxPhys) &&
               !float.IsInfinity(candidate.dxPhys) &&
               !float.IsNaN(candidate.tauFluidMin) &&
               !float.IsInfinity(candidate.tauFluidMin) &&
               !float.IsNaN(candidate.tauThermalMin) &&
               !float.IsInfinity(candidate.tauThermalMin);
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Preserve the original save error. A stale .tmp file is recoverable.
        }
    }
}

public sealed class InMemorySimulationSetupCaseStore : ISimulationSetupCaseStore
{
    private readonly List<SimulationSetupUserCase> storedCases =
        new List<SimulationSetupUserCase>();

    public string DisplayLocation => "In-memory validation store";

    public List<SimulationSetupUserCase> Load(out string issue)
    {
        issue = string.Empty;
        return CloneCases(storedCases);
    }

    public bool Save(IReadOnlyList<SimulationSetupUserCase> cases, out string issue)
    {
        storedCases.Clear();
        if (cases != null)
        {
            for (int i = 0; i < cases.Count; i++)
                storedCases.Add(cases[i].Clone());
        }

        issue = string.Empty;
        return true;
    }

    private static List<SimulationSetupUserCase> CloneCases(
        IReadOnlyList<SimulationSetupUserCase> source)
    {
        var result = new List<SimulationSetupUserCase>(source.Count);
        for (int i = 0; i < source.Count; i++)
            result.Add(source[i].Clone());
        return result;
    }
}
