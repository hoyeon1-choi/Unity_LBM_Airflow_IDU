using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public readonly struct SimulationSetupModelDefinition
{
    public string Id { get; }
    public string Name { get; }
    public string SceneName { get; }
    public string SourcePath { get; }
    public string DomainName { get; }
    public Vector3 DomainCenter { get; }
    public Vector3 DomainSize { get; }
    public uint Nx { get; }
    public uint Ny { get; }
    public uint Nz { get; }
    public int RendererCount { get; }
    public int ColliderCount { get; }
    public int MeshCount { get; }
    public long VertexCount { get; }
    public long TriangleCount { get; }
    public int BoundaryPatchCount { get; }
    public int AcSourceCount { get; }
    public int ObstacleCount { get; }
    public bool IsActiveScene { get; }
    public SimulationController Controller { get; }

    public SimulationSetupModelDefinition(
        string id, string name, string sceneName, string sourcePath, string domainName,
        Vector3 domainCenter, Vector3 domainSize, uint nx, uint ny, uint nz,
        int rendererCount, int colliderCount, int meshCount, long vertexCount,
        long triangleCount, int boundaryPatchCount, int acSourceCount, int obstacleCount,
        bool isActiveScene, SimulationController controller = null)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        SceneName = sceneName ?? string.Empty;
        SourcePath = sourcePath ?? string.Empty;
        DomainName = domainName ?? string.Empty;
        DomainCenter = domainCenter;
        DomainSize = domainSize;
        Nx = nx;
        Ny = ny;
        Nz = nz;
        RendererCount = rendererCount;
        ColliderCount = colliderCount;
        MeshCount = meshCount;
        VertexCount = vertexCount;
        TriangleCount = triangleCount;
        BoundaryPatchCount = boundaryPatchCount;
        AcSourceCount = acSourceCount;
        ObstacleCount = obstacleCount;
        IsActiveScene = isActiveScene;
        Controller = controller;
    }
}

public interface ISimulationSetupModelSource
{
    string DisplayName { get; }
    IReadOnlyList<SimulationSetupModelDefinition> LoadAvailableModels(out string issue);
}

public sealed class LoadedSimulationSceneModelSource : ISimulationSetupModelSource
{
    public string DisplayName => "Unity Scene / SimulationController";

    public IReadOnlyList<SimulationSetupModelDefinition> LoadAvailableModels(out string issue)
    {
        try
        {
            SimulationController[] controllers = UnityEngine.Object.FindObjectsByType<SimulationController>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            var models = new List<SimulationSetupModelDefinition>(controllers.Length);
            Scene activeScene = SceneManager.GetActiveScene();
            for (int i = 0; i < controllers.Length; i++)
            {
                SimulationController controller = controllers[i];
                if (controller == null)
                    continue;

                Scene scene = controller.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                models.Add(BuildDefinition(controller, scene, activeScene.handle == scene.handle));
            }

            models.Sort(CompareModels);
            issue = string.Empty;
            return models;
        }
        catch (Exception exception)
        {
            issue = $"Loaded simulation model scan failed: {exception.Message}";
            return Array.Empty<SimulationSetupModelDefinition>();
        }
    }

    private static SimulationSetupModelDefinition BuildDefinition(
        SimulationController controller, Scene scene, bool isActiveScene)
    {
        Transform domain = controller.DomainRoot;
        Vector3 domainCenter = domain != null ? domain.position : Vector3.zero;
        Vector3 domainSize = domain != null ? Abs(domain.localScale) : Vector3.zero;
        string domainName = domain != null ? domain.name : "Missing DomainRoot";
        string modelName = domain != null ? domainName : controller.gameObject.name;

        int rendererCount = 0;
        int colliderCount = 0;
        int meshCount = 0;
        int boundaryPatchCount = 0;
        int acSourceCount = 0;
        int obstacleCount = 0;
        long vertexCount = 0L;
        long triangleCount = 0L;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            rendererCount += root.GetComponentsInChildren<Renderer>(true).Length;
            colliderCount += root.GetComponentsInChildren<Collider>(true).Length;
            boundaryPatchCount += root.GetComponentsInChildren<LBMZouHeBox>(true).Length;
            acSourceCount += root.GetComponentsInChildren<ACSource>(true).Length;
            obstacleCount += root.GetComponentsInChildren<DeviceObstacles>(true).Length;
            obstacleCount += root.GetComponentsInChildren<Racks>(true).Length;

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            meshCount += filters.Length;
            for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
            {
                Mesh mesh = filters[filterIndex].sharedMesh;
                if (mesh == null)
                    continue;

                vertexCount += mesh.vertexCount;
                for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                    triangleCount += (long)mesh.GetIndexCount(subMeshIndex) / 3L;
            }
        }

        string scenePath = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
        return new SimulationSetupModelDefinition(
            scenePath + ":" + BuildHierarchyPath(controller.transform) + ":" + controller.GetInstanceID(),
            modelName, scene.name, scenePath, domainName, domainCenter, domainSize,
            controller.Nx, controller.Ny, controller.Nz,
            rendererCount, colliderCount, meshCount, vertexCount, triangleCount,
            boundaryPatchCount, acSourceCount, obstacleCount, isActiveScene, controller);
    }

    private static int CompareModels(SimulationSetupModelDefinition left, SimulationSetupModelDefinition right)
    {
        int activeOrder = right.IsActiveScene.CompareTo(left.IsActiveScene);
        if (activeOrder != 0)
            return activeOrder;

        int sceneOrder = string.Compare(left.SceneName, right.SceneName, StringComparison.OrdinalIgnoreCase);
        return sceneOrder != 0
            ? sceneOrder
            : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHierarchyPath(Transform target)
    {
        string path = target.name;
        while (target.parent != null)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }

        return path;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}

public sealed class InMemorySimulationSetupModelSource : ISimulationSetupModelSource
{
    private readonly List<SimulationSetupModelDefinition> models;

    public string DisplayName { get; }

    public InMemorySimulationSetupModelSource(
        IReadOnlyList<SimulationSetupModelDefinition> definitions,
        string displayName = "Validation model source")
    {
        DisplayName = displayName;
        models = definitions != null
            ? new List<SimulationSetupModelDefinition>(definitions)
            : new List<SimulationSetupModelDefinition>();
    }

    public IReadOnlyList<SimulationSetupModelDefinition> LoadAvailableModels(out string issue)
    {
        issue = string.Empty;
        return new List<SimulationSetupModelDefinition>(models);
    }
}

public sealed class SimulationSetupModelController : IDisposable
{
    private const string SelectedRowClassName = "model-list-item--selected";
    private readonly List<SimulationSetupModelDefinition> models = new List<SimulationSetupModelDefinition>();
    private readonly Dictionary<string, Button> rows = new Dictionary<string, Button>();
    private readonly Dictionary<string, Action> rowHandlers = new Dictionary<string, Action>();

    private ISimulationSetupModelSource source;
    private ScrollView modelList;
    private Button refreshButton;
    private Label modelCountValue;
    private Label selectedStateValue;
    private Label selectedNameValue;
    private Label selectedDescriptionValue;
    private Label selectedSceneValue;
    private Label selectedSourceValue;
    private Label selectedDomainValue;
    private Label selectedSizeValue;
    private Label selectedCenterValue;
    private Label selectedGridValue;
    private Label selectedGeometryValue;
    private Label selectedTopologyValue;
    private Label selectedComponentsValue;
    private Label selectionStatus;
    private SimulationSetupModelDefinition selectedModel;
    private bool hasSelection;
    private bool dirty = true;

    public int ModelCount => models.Count;
    public bool HasSelection => hasSelection;
    public string SelectedModelId => hasSelection ? selectedModel.Id : string.Empty;
    public SimulationSetupModelDefinition SelectedModel => selectedModel;
    public SimulationController SelectedController => hasSelection ? selectedModel.Controller : null;

    public event Action<SimulationSetupModelDefinition> SelectedModelChanged;

    public bool Initialize(VisualElement documentRoot, ISimulationSetupModelSource modelSource, out string issue)
    {
        Dispose();
        if (documentRoot == null || modelSource == null)
        {
            issue = "Model UI root or model source is missing.";
            return false;
        }

        modelList = documentRoot.Q<ScrollView>("ModelListScrollView");
        refreshButton = documentRoot.Q<Button>("RefreshModelListButton");
        modelCountValue = documentRoot.Q<Label>("ModelCountValue");
        selectedStateValue = documentRoot.Q<Label>("SelectedModelStateValue");
        selectedNameValue = documentRoot.Q<Label>("SelectedModelNameValue");
        selectedDescriptionValue = documentRoot.Q<Label>("SelectedModelDescriptionValue");
        selectedSceneValue = documentRoot.Q<Label>("SelectedModelSceneValue");
        selectedSourceValue = documentRoot.Q<Label>("SelectedModelSourceValue");
        selectedDomainValue = documentRoot.Q<Label>("SelectedModelDomainValue");
        selectedSizeValue = documentRoot.Q<Label>("SelectedModelSizeValue");
        selectedCenterValue = documentRoot.Q<Label>("SelectedModelCenterValue");
        selectedGridValue = documentRoot.Q<Label>("SelectedModelGridValue");
        selectedGeometryValue = documentRoot.Q<Label>("SelectedModelGeometryValue");
        selectedTopologyValue = documentRoot.Q<Label>("SelectedModelTopologyValue");
        selectedComponentsValue = documentRoot.Q<Label>("SelectedModelComponentsValue");
        selectionStatus = documentRoot.Q<Label>("ModelSelectionStatus");

        if (modelList == null || refreshButton == null || modelCountValue == null ||
            selectedStateValue == null || selectedNameValue == null || selectedDescriptionValue == null ||
            selectedSceneValue == null || selectedSourceValue == null || selectedDomainValue == null ||
            selectedSizeValue == null || selectedCenterValue == null || selectedGridValue == null ||
            selectedGeometryValue == null || selectedTopologyValue == null ||
            selectedComponentsValue == null || selectionStatus == null)
        {
            issue = "One or more Model Selection UI elements are missing.";
            Dispose();
            return false;
        }

        source = modelSource;
        refreshButton.clicked += Refresh;
        refreshButton.tooltip = "Rescan SimulationController components in currently loaded Unity scenes.";
        ShowWaitingState();
        issue = string.Empty;
        return true;
    }

    public void MarkDirty()
    {
        dirty = true;
    }

    public void RefreshIfNeeded()
    {
        if (dirty)
            Refresh();
    }

    public void Refresh()
    {
        if (source == null)
            return;

        string previousSelectionId = SelectedModelId;
        IReadOnlyList<SimulationSetupModelDefinition> loaded = source.LoadAvailableModels(out string issue);
        models.Clear();
        var knownIds = new HashSet<string>(StringComparer.Ordinal);
        if (loaded != null)
        {
            for (int i = 0; i < loaded.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(loaded[i].Id) && knownIds.Add(loaded[i].Id))
                    models.Add(loaded[i]);
            }
        }

        dirty = false;
        BuildModelList();
        if (!string.IsNullOrEmpty(issue))
        {
            ClearSelection();
            SetStatus(issue, true);
            return;
        }

        if (models.Count == 0)
        {
            ClearSelection();
            SetStatus("No loaded SimulationController model. Waiting for the Bootstrap LBM scene.", false);
            return;
        }

        int selectedIndex = FindModelIndex(previousSelectionId);
        if (selectedIndex < 0)
            selectedIndex = 0;
        SelectModel(models[selectedIndex].Id, false);
        SetStatus(
            $"Connected to {source.DisplayName}. Selection is retained for setup and applied to the solver in F10.",
            false);
    }

    public bool SelectModel(string id)
    {
        return SelectModel(id, true);
    }

    public void Dispose()
    {
        if (refreshButton != null)
            refreshButton.clicked -= Refresh;

        ClearRows();
        models.Clear();
        source = null;
        modelList = null;
        refreshButton = null;
        modelCountValue = null;
        selectedStateValue = null;
        selectedNameValue = null;
        selectedDescriptionValue = null;
        selectedSceneValue = null;
        selectedSourceValue = null;
        selectedDomainValue = null;
        selectedSizeValue = null;
        selectedCenterValue = null;
        selectedGridValue = null;
        selectedGeometryValue = null;
        selectedTopologyValue = null;
        selectedComponentsValue = null;
        selectionStatus = null;
        selectedModel = default;
        hasSelection = false;
        dirty = true;
        SelectedModelChanged = null;
    }

    private void BuildModelList()
    {
        ClearRows();
        modelCountValue.text = models.Count == 1 ? "1 model" : $"{models.Count} models";
        for (int i = 0; i < models.Count; i++)
        {
            SimulationSetupModelDefinition model = models[i];
            var row = new Button { name = "ModelListItem_" + SanitizeName(model.Id) };
            row.AddToClassList("model-list-item");
            row.tooltip = model.SourcePath;

            var nameLabel = new Label(model.Name);
            nameLabel.AddToClassList("model-list-item__name");
            row.Add(nameLabel);

            string active = model.IsActiveScene ? "Active scene | " : string.Empty;
            var summaryLabel = new Label($"{active}{model.SceneName} | {FormatVector(model.DomainSize)} m");
            summaryLabel.AddToClassList("model-list-item__summary");
            row.Add(summaryLabel);

            string capturedId = model.Id;
            Action handler = () => SelectModel(capturedId);
            row.clicked += handler;
            rows.Add(model.Id, row);
            rowHandlers.Add(model.Id, handler);
            modelList.Add(row);
        }
    }

    private bool SelectModel(string id, bool notify)
    {
        int index = FindModelIndex(id);
        if (index < 0)
            return false;

        selectedModel = models[index];
        hasSelection = true;
        foreach (KeyValuePair<string, Button> entry in rows)
            entry.Value.EnableInClassList(SelectedRowClassName, entry.Key == selectedModel.Id);

        selectedStateValue.text = selectedModel.Controller == null ? "Metadata" : "Loaded";
        selectedStateValue.EnableInClassList("model-selection__badge--waiting", false);
        selectedNameValue.text = selectedModel.Name;
        selectedDescriptionValue.text =
            "Existing Unity scene geometry referenced by SimulationController.DomainRoot.";
        selectedSceneValue.text = selectedModel.SceneName;
        selectedSceneValue.tooltip = selectedModel.SourcePath;
        selectedSourceValue.text = source.DisplayName;
        selectedSourceValue.tooltip = selectedModel.SourcePath;
        selectedDomainValue.text = selectedModel.DomainName;
        selectedSizeValue.text = FormatVector(selectedModel.DomainSize) + " m";
        selectedCenterValue.text = FormatVector(selectedModel.DomainCenter) + " m";
        selectedGridValue.text = selectedModel.Nx > 0 && selectedModel.Ny > 0 && selectedModel.Nz > 0
            ? $"{selectedModel.Nx} x {selectedModel.Ny} x {selectedModel.Nz}"
            : "Pending solver scaling";
        selectedGeometryValue.text =
            $"{selectedModel.RendererCount} renderers / {selectedModel.ColliderCount} colliders / {selectedModel.MeshCount} meshes";
        selectedTopologyValue.text =
            $"{selectedModel.VertexCount:N0} vertices / {selectedModel.TriangleCount:N0} triangles";
        selectedComponentsValue.text =
            $"{selectedModel.BoundaryPatchCount} patches / {selectedModel.AcSourceCount} AC / {selectedModel.ObstacleCount} obstacles";

        if (notify)
            SelectedModelChanged?.Invoke(selectedModel);
        return true;
    }

    private int FindModelIndex(string id)
    {
        if (string.IsNullOrEmpty(id))
            return -1;

        for (int i = 0; i < models.Count; i++)
        {
            if (string.Equals(models[i].Id, id, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void ClearSelection()
    {
        selectedModel = default;
        hasSelection = false;
        foreach (Button row in rows.Values)
            row.EnableInClassList(SelectedRowClassName, false);
        ShowWaitingState();
    }

    private void ShowWaitingState()
    {
        if (selectedStateValue == null)
            return;

        selectedStateValue.text = "Waiting";
        selectedStateValue.EnableInClassList("model-selection__badge--waiting", true);
        selectedNameValue.text = "No loaded model";
        selectedDescriptionValue.text =
            "Open the Model step after the Bootstrap scene loads the LBM scene.";
        selectedSceneValue.text = "—";
        selectedSceneValue.tooltip = string.Empty;
        selectedSourceValue.text = source != null ? source.DisplayName : "Unity Scene";
        selectedSourceValue.tooltip = string.Empty;
        selectedDomainValue.text = "—";
        selectedSizeValue.text = "—";
        selectedCenterValue.text = "—";
        selectedGridValue.text = "—";
        selectedGeometryValue.text = "—";
        selectedTopologyValue.text = "—";
        selectedComponentsValue.text = "—";
    }

    private void SetStatus(string message, bool isError)
    {
        selectionStatus.text = message ?? string.Empty;
        selectionStatus.EnableInClassList("model-selection__status--error", isError);
    }

    private void ClearRows()
    {
        foreach (KeyValuePair<string, Action> entry in rowHandlers)
        {
            if (rows.TryGetValue(entry.Key, out Button row))
                row.clicked -= entry.Value;
        }

        rowHandlers.Clear();
        rows.Clear();
        modelList?.Clear();
    }

    private static string FormatVector(Vector3 value)
    {
        return $"{value.x:0.###} x {value.y:0.###} x {value.z:0.###}";
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Unknown";

        char[] characters = value.ToCharArray();
        for (int i = 0; i < characters.Length; i++)
        {
            char character = characters[i];
            if (!char.IsLetterOrDigit(character) && character != '_')
                characters[i] = '_';
        }

        return new string(characters);
    }
}
