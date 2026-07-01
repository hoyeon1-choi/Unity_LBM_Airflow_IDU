using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

[Serializable]
public class FmuVariableInfo
{
    public string name;
    public uint valueReference;
    public SignalDirection causality;
    public string variability;
    public SignalValueType valueType;
    public bool hasStartReal;
    public double startReal;
}

public class FmuModelDescription
{
    private readonly Dictionary<string, FmuVariableInfo> byName =
        new Dictionary<string, FmuVariableInfo>(StringComparer.Ordinal);
    private readonly Dictionary<uint, FmuVariableInfo> byValueReference =
        new Dictionary<uint, FmuVariableInfo>();

    public string modelName = string.Empty;
    public string guid = string.Empty;
    public string modelIdentifier = string.Empty;
    public readonly List<FmuVariableInfo> variables = new List<FmuVariableInfo>();

    public IReadOnlyList<FmuVariableInfo> Variables => variables;

    public void RebuildLookup()
    {
        byName.Clear();
        byValueReference.Clear();

        for (int i = 0; i < variables.Count; i++)
        {
            FmuVariableInfo variable = variables[i];
            if (variable == null || string.IsNullOrEmpty(variable.name))
                continue;

            byName[variable.name] = variable;

            if (!byValueReference.ContainsKey(variable.valueReference))
                byValueReference.Add(variable.valueReference, variable);
        }
    }

    public bool TryGetVariable(string variableName, out FmuVariableInfo variable)
    {
        return byName.TryGetValue(variableName ?? string.Empty, out variable);
    }

    public bool TryGetVariable(uint valueReference, out FmuVariableInfo variable)
    {
        return byValueReference.TryGetValue(valueReference, out variable);
    }

    public bool TryGetValueReference(string variableName, out uint valueReference)
    {
        FmuVariableInfo variable;
        if (TryGetVariable(variableName, out variable))
        {
            valueReference = variable.valueReference;
            return true;
        }

        valueReference = 0u;
        return false;
    }

    public bool TryGetVariableName(uint valueReference, out string variableName)
    {
        FmuVariableInfo variable;
        if (TryGetVariable(valueReference, out variable))
        {
            variableName = variable.name;
            return true;
        }

        variableName = string.Empty;
        return false;
    }
}

public static class FmuModelDescriptionParser
{
    public static bool TryResolveFmuSourcePath(
        string streamingAssetsFmuRoot,
        string fmuFileName,
        string modelId,
        out string sourcePath,
        out string status)
    {
        sourcePath = string.Empty;
        status = string.Empty;

        if (string.IsNullOrEmpty(streamingAssetsFmuRoot) || !Directory.Exists(streamingAssetsFmuRoot))
        {
            status = $"FMU root directory not found: {streamingAssetsFmuRoot}";
            return false;
        }

        string safeFileName = string.IsNullOrEmpty(fmuFileName) ? $"{modelId}.fmu" : fmuFileName;
        string directPath = Path.Combine(streamingAssetsFmuRoot, safeFileName);
        if (File.Exists(directPath) || Directory.Exists(directPath))
        {
            sourcePath = directPath;
            status = $"Resolved direct FMU source: {sourcePath}";
            return true;
        }

        string fileNameOnly = Path.GetFileName(safeFileName);
        if (!string.IsNullOrEmpty(fileNameOnly))
        {
            string[] matches = Directory.GetFiles(streamingAssetsFmuRoot, fileNameOnly, SearchOption.AllDirectories);
            if (matches.Length > 0)
            {
                sourcePath = matches[0];
                status = $"Resolved FMU file by recursive search: {sourcePath}";
                return true;
            }
        }

        string desiredModelName = Path.GetFileNameWithoutExtension(fileNameOnly);
        if (string.IsNullOrEmpty(desiredModelName))
            desiredModelName = modelId;

        string[] xmlFiles = Directory.GetFiles(streamingAssetsFmuRoot, "modelDescription.xml", SearchOption.AllDirectories);
        for (int i = 0; i < xmlFiles.Length; i++)
        {
            string xml = xmlFiles[i];
            string modelName;
            if (!TryReadModelName(xml, out modelName))
                continue;

            if (string.Equals(modelName, desiredModelName, StringComparison.Ordinal) ||
                string.Equals(modelName, modelId, StringComparison.Ordinal))
            {
                sourcePath = Path.GetDirectoryName(xml);
                status = $"Resolved expanded FMU directory by modelName={modelName}: {sourcePath}";
                return true;
            }
        }

        status = $"FMU source not found. root={streamingAssetsFmuRoot}, fmuFileName={safeFileName}, modelId={modelId}";
        return false;
    }

    public static string PrepareUnzipDirectory(string fmuSourcePath, string cacheRoot, string preferredModelName)
    {
        if (string.IsNullOrEmpty(fmuSourcePath))
            throw new ArgumentException("FMU source path is empty.", nameof(fmuSourcePath));

        if (Directory.Exists(fmuSourcePath))
            return fmuSourcePath;

        if (!File.Exists(fmuSourcePath))
            throw new FileNotFoundException("FMU source file was not found.", fmuSourcePath);

        string cacheName = GetSafeCacheName(
            string.IsNullOrEmpty(preferredModelName)
                ? Path.GetFileNameWithoutExtension(fmuSourcePath)
                : preferredModelName);
        string revisionSuffix = GetArchiveRevisionSuffix(fmuSourcePath);
        if (!string.IsNullOrEmpty(revisionSuffix))
            cacheName = $"{cacheName}_{revisionSuffix}";

        string unzipDirectory = Path.Combine(cacheRoot, cacheName);
        if (Directory.Exists(unzipDirectory) &&
            File.Exists(Path.Combine(unzipDirectory, "modelDescription.xml")))
        {
            return unzipDirectory;
        }

        if (Directory.Exists(unzipDirectory))
            Directory.Delete(unzipDirectory, true);

        Directory.CreateDirectory(unzipDirectory);
        ExtractArchiveToDirectory(fmuSourcePath, unzipDirectory);
        return unzipDirectory;
    }

    public static FmuModelDescription ParseFromDirectory(string unzipDirectory)
    {
        if (string.IsNullOrEmpty(unzipDirectory))
            throw new ArgumentException("FMU unzip directory is empty.", nameof(unzipDirectory));

        string xmlPath = Path.Combine(unzipDirectory, "modelDescription.xml");
        return ParseModelDescription(xmlPath);
    }

    public static FmuModelDescription ParseModelDescription(string xmlPath)
    {
        if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
            throw new FileNotFoundException("modelDescription.xml was not found.", xmlPath);

        XDocument document = XDocument.Load(xmlPath);
        XElement root = document.Root;
        if (root == null)
            throw new InvalidDataException($"Invalid modelDescription.xml: {xmlPath}");

        FmuModelDescription description = new FmuModelDescription
        {
            modelName = ReadAttribute(root, "modelName"),
            guid = ReadAttribute(root, "guid")
        };

        foreach (XElement element in document.Descendants())
        {
            if (element.Name.LocalName == "CoSimulation")
            {
                description.modelIdentifier = ReadAttribute(element, "modelIdentifier");
                break;
            }
        }

        foreach (XElement scalar in document.Descendants())
        {
            if (scalar.Name.LocalName != "ScalarVariable")
                continue;

            string name = ReadAttribute(scalar, "name");
            string vrText = ReadAttribute(scalar, "valueReference");
            uint valueReference;
            if (string.IsNullOrEmpty(name) ||
                !uint.TryParse(vrText, NumberStyles.Integer, CultureInfo.InvariantCulture, out valueReference))
            {
                continue;
            }

            XElement typeElement = FindValueTypeElement(scalar);
            SignalValueType valueType = ReadValueType(typeElement);

            FmuVariableInfo variable = new FmuVariableInfo
            {
                name = name,
                valueReference = valueReference,
                causality = ReadCausality(ReadAttribute(scalar, "causality")),
                variability = ReadAttribute(scalar, "variability"),
                valueType = valueType
            };

            if (typeElement != null && valueType == SignalValueType.Real)
            {
                string start = ReadAttribute(typeElement, "start");
                double startReal;
                if (double.TryParse(start, NumberStyles.Float, CultureInfo.InvariantCulture, out startReal))
                {
                    variable.hasStartReal = true;
                    variable.startReal = startReal;
                }
            }

            description.variables.Add(variable);
        }

        description.RebuildLookup();
        return description;
    }

    private static bool TryReadModelName(string xmlPath, out string modelName)
    {
        modelName = string.Empty;
        try
        {
            XDocument document = XDocument.Load(xmlPath);
            if (document.Root == null)
                return false;

            modelName = ReadAttribute(document.Root, "modelName");
            return !string.IsNullOrEmpty(modelName);
        }
        catch
        {
            return false;
        }
    }

    private static void ExtractArchiveToDirectory(string archivePath, string destinationDirectory)
    {
        string destinationFullPath = Path.GetFullPath(destinationDirectory);
        if (!destinationFullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            destinationFullPath += Path.DirectorySeparatorChar;

        using (ZipArchive archive = ZipFile.OpenRead(archivePath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
                if (!targetPath.StartsWith(destinationFullPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsafe FMU archive entry path: {entry.FullName}");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                string targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                entry.ExtractToFile(targetPath, true);
            }
        }
    }

    private static string GetSafeCacheName(string value)
    {
        string text = string.IsNullOrEmpty(value) ? "FMU" : value;
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            text = text.Replace(invalid[i], '_');

        return text;
    }

    private static string GetArchiveRevisionSuffix(string archivePath)
    {
        try
        {
            FileInfo info = new FileInfo(archivePath);
            return $"{info.Length}_{info.LastWriteTimeUtc.Ticks:x}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadAttribute(XElement element, string name)
    {
        if (element == null)
            return string.Empty;

        XAttribute attribute = element.Attribute(name);
        return attribute != null ? attribute.Value : string.Empty;
    }

    private static XElement FindValueTypeElement(XElement scalar)
    {
        foreach (XElement child in scalar.Elements())
        {
            string localName = child.Name.LocalName;
            if (localName == "Real" ||
                localName == "Integer" ||
                localName == "Boolean" ||
                localName == "String")
            {
                return child;
            }
        }

        return null;
    }

    private static SignalValueType ReadValueType(XElement typeElement)
    {
        if (typeElement == null)
            return SignalValueType.Real;

        switch (typeElement.Name.LocalName)
        {
            case "Integer":
                return SignalValueType.Integer;
            case "Boolean":
                return SignalValueType.Boolean;
            case "String":
                return SignalValueType.String;
            default:
                return SignalValueType.Real;
        }
    }

    private static SignalDirection ReadCausality(string causality)
    {
        switch (causality)
        {
            case "input":
                return SignalDirection.Input;
            case "output":
                return SignalDirection.Output;
            case "parameter":
                return SignalDirection.Parameter;
            default:
                return SignalDirection.Local;
        }
    }
}
