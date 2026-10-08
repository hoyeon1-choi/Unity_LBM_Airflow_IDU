using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public enum SimulationSetupBoundaryCategory
{
    Inlet,
    Outlet,
    Wall
}

public sealed class SimulationSetupBoundaryDefinition
{
    public string Id { get; }
    public string Name { get; }
    public SimulationSetupBoundaryCategory Category { get; }
    public string SourceType { get; }
    public bool Enabled { get; }
    public string GridPatch { get; }
    public string InputMode { get; }
    public Vector3 VelocityPhys { get; }
    public float VolumeFlowRateM3ps { get; }
    public float TemperatureDegC { get; }
    public float DischargeAngleDeg { get; }
    public string Normal { get; }
    public float AreaM2 { get; }
    public bool MassFluxCorrection { get; }
    public float DensityTarget { get; }
    public float NormalVelocityBlend { get; }
    public float DensityAnchor { get; }
    public float TargetFlowRateM3ps { get; }
    public string MomentumBoundary { get; }
    public ThermalBoundaryType ThermalBoundary { get; }
    public bool IsImplicitDomainWall { get; }
    public UnityEngine.Object SourceObject { get; }

    public SimulationSetupBoundaryDefinition(
        string id, string name, SimulationSetupBoundaryCategory category, string sourceType,
        bool enabled, string gridPatch, string inputMode, Vector3 velocityPhys,
        float volumeFlowRateM3ps, float temperatureDegC, string normal, float areaM2,
        bool massFluxCorrection, float densityTarget, float normalVelocityBlend,
        float densityAnchor, float targetFlowRateM3ps, string momentumBoundary,
        ThermalBoundaryType thermalBoundary, bool isImplicitDomainWall,
        UnityEngine.Object sourceObject = null, float dischargeAngleDeg = 45f)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        Category = category;
        SourceType = sourceType ?? string.Empty;
        Enabled = enabled;
        GridPatch = gridPatch ?? string.Empty;
        InputMode = inputMode ?? string.Empty;
        VelocityPhys = velocityPhys;
        VolumeFlowRateM3ps = volumeFlowRateM3ps;
        TemperatureDegC = temperatureDegC;
        DischargeAngleDeg = dischargeAngleDeg;
        Normal = normal ?? string.Empty;
        AreaM2 = areaM2;
        MassFluxCorrection = massFluxCorrection;
        DensityTarget = densityTarget;
        NormalVelocityBlend = normalVelocityBlend;
        DensityAnchor = densityAnchor;
        TargetFlowRateM3ps = targetFlowRateM3ps;
        MomentumBoundary = momentumBoundary ?? string.Empty;
        ThermalBoundary = thermalBoundary;
        IsImplicitDomainWall = isImplicitDomainWall;
        SourceObject = sourceObject;
    }
}

public interface ISimulationSetupBoundarySource
{
    string DisplayName { get; }
    IReadOnlyList<SimulationSetupBoundaryDefinition> LoadBoundaries(
        SimulationSetupModelDefinition model, out string issue);
}

public sealed class LoadedSimulationBoundarySource : ISimulationSetupBoundarySource
{
    private bool loggedGlobalFallback;
    private bool loggedEmptyScan;

    public string DisplayName => "Selected model scene components";

    public IReadOnlyList<SimulationSetupBoundaryDefinition> LoadBoundaries(
        SimulationSetupModelDefinition model, out string issue)
    {
        if (model.Controller == null)
        {
            issue = "The selected model has no loaded SimulationController binding.";
            return Array.Empty<SimulationSetupBoundaryDefinition>();
        }

        Scene scene = model.Controller.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            issue = "The selected model scene is not loaded.";
            return Array.Empty<SimulationSetupBoundaryDefinition>();
        }

        try
        {
            var result = new List<SimulationSetupBoundaryDefinition>();
            GameObject[] roots = scene.GetRootGameObjects();
            var patchIds = new HashSet<int>();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                LBMZouHeBox[] patches = roots[rootIndex].GetComponentsInChildren<LBMZouHeBox>(true);
                for (int patchIndex = 0; patchIndex < patches.Length; patchIndex++)
                {
                    LBMZouHeBox patch = patches[patchIndex];
                    if (patch != null && patchIds.Add(patch.GetInstanceID()))
                        result.Add(BuildPatchDefinition(patch));
                }
            }

            // Bootstrap loads the LBM scene additively. During scene integration or an
            // Editor domain reload, a controller-scoped root scan can temporarily miss
            // patches even though the solver's global scene cache can already see them.
            // Match the solver discovery path as a safe fallback; solver components on
            // display-only clones are stripped, so they cannot be selected here.
            if (patchIds.Count == 0)
            {
                SimulationSceneCache solverCache = model.Controller.GetComponent<SimulationSceneCache>();
                LBMZouHeBox[] cachedPatches = solverCache != null
                    ? solverCache.ZouHeBoxes
                    : Array.Empty<LBMZouHeBox>();
                for (int patchIndex = 0; patchIndex < cachedPatches.Length; patchIndex++)
                {
                    LBMZouHeBox patch = cachedPatches[patchIndex];
                    if (patch != null && patchIds.Add(patch.GetInstanceID()))
                        result.Add(BuildPatchDefinition(patch));
                }

                LBMZouHeBox[] loadedPatches = UnityEngine.Object.FindObjectsByType<LBMZouHeBox>(
                    FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                for (int patchIndex = 0; patchIndex < loadedPatches.Length; patchIndex++)
                {
                    LBMZouHeBox patch = loadedPatches[patchIndex];
                    if (patch == null || !patch.gameObject.scene.IsValid() ||
                        !patch.gameObject.scene.isLoaded || !patchIds.Add(patch.GetInstanceID()))
                        continue;
                    result.Add(BuildPatchDefinition(patch));
                }

                if (patchIds.Count > 0 && !loggedGlobalFallback)
                {
                    loggedGlobalFallback = true;
                    Debug.LogWarning(
                        $"[UX02][F10][Case={model.Name}] Controller-scoped Boundary scan returned 0 patches; " +
                        $"bound {patchIds.Count} loaded LBMZouHeBox patches through the solver-compatible fallback.");
                }
                else if (patchIds.Count == 0 && !loggedEmptyScan)
                {
                    loggedEmptyScan = true;
                    Debug.LogWarning(
                        $"[UX02][F10][Case={model.Name}] Boundary scan found 0 LBMZouHeBox patches " +
                        $"in the selected scene, SimulationSceneCache, and all loaded scenes. Retrying after scene integration.");
                }
            }

            if (model.Controller.DomainRoot != null)
            {
                result.Add(new SimulationSetupBoundaryDefinition(
                    "domain:" + model.Id, model.Controller.DomainRoot.name + " outer shell",
                    SimulationSetupBoundaryCategory.Wall, "Compute shader domain shell", true,
                    "x/y/z min & max grid planes", string.Empty, Vector3.zero, 0f, 0f,
                    string.Empty, 0f, false, 0f, 0f, 0f, 0f,
                    "Resting half-way bounce-back", ThermalBoundaryType.Adiabatic, true));
            }

            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                DeviceObstacles[] obstacles =
                    roots[rootIndex].GetComponentsInChildren<DeviceObstacles>(true);
                for (int obstacleIndex = 0; obstacleIndex < obstacles.Length; obstacleIndex++)
                {
                    DeviceObstacles obstacle = obstacles[obstacleIndex];
                    result.Add(new SimulationSetupBoundaryDefinition(
                        "wall:" + obstacle.GetInstanceID(), obstacle.name,
                        SimulationSetupBoundaryCategory.Wall, nameof(DeviceObstacles),
                        obstacle.gameObject.activeInHierarchy, FormatGridPatch(obstacle.MinIdx, obstacle.MaxIdx),
                        string.Empty, Vector3.zero, 0f, obstacle.Temperature, string.Empty, 0f,
                        false, 0f, 0f, 0f, 0f, "Resting half-way bounce-back",
                        obstacle.BoundaryType, false, obstacle));
                }
            }

            result.Sort(CompareDefinitions);
            issue = string.Empty;
            return result;
        }
        catch (Exception exception)
        {
            issue = $"Boundary component scan failed: {exception.Message}";
            return Array.Empty<SimulationSetupBoundaryDefinition>();
        }
    }

    private static SimulationSetupBoundaryDefinition BuildPatchDefinition(LBMZouHeBox patch)
    {
        SimulationSetupBoundaryCategory category = patch.PatchKind == LBMZouHeBox.Kind.Inlet
            ? SimulationSetupBoundaryCategory.Inlet
            : SimulationSetupBoundaryCategory.Outlet;
        return new SimulationSetupBoundaryDefinition(
            "zouhe:" + patch.GetInstanceID(), patch.name, category, nameof(LBMZouHeBox),
            patch.Power, FormatGridPatch(patch.MinIdx, patch.MaxIdx), patch.InputMode.ToString(),
            patch.WindSpeedPhysVector3, patch.TargetFlowRateM3psCached,
            patch.InletTemperatureDegC, FormatNormal(patch.NormalAxis, patch.NormalSign),
            patch.PatchAreaPhysCached, patch.EnableMassFluxCorrection, patch.BaseRhoOut,
            patch.OutletNormalVelocityBlend, patch.OutletRhoAnchor,
            patch.TargetFlowRateM3psCached, string.Empty, ThermalBoundaryType.Adiabatic,
            false, patch, patch.DischargeAngleDeg);
    }

    private static int CompareDefinitions(
        SimulationSetupBoundaryDefinition left, SimulationSetupBoundaryDefinition right)
    {
        int category = left.Category.CompareTo(right.Category);
        return category != 0
            ? category
            : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatNormal(LBMZouHeBox.Axis axis, LBMZouHeBox.Sign sign)
    {
        return (sign == LBMZouHeBox.Sign.Positive ? "+" : "-") + axis;
    }

    private static string FormatGridPatch(Unity.Mathematics.uint3 min, Unity.Mathematics.uint3 max)
    {
        return $"[{min.x},{min.y},{min.z}] - [{max.x},{max.y},{max.z}]";
    }
}

public sealed class InMemorySimulationSetupBoundarySource : ISimulationSetupBoundarySource
{
    private readonly List<SimulationSetupBoundaryDefinition> definitions;
    public string DisplayName { get; }

    public InMemorySimulationSetupBoundarySource(
        IReadOnlyList<SimulationSetupBoundaryDefinition> sourceDefinitions,
        string displayName = "Validation boundary source")
    {
        definitions = sourceDefinitions != null
            ? new List<SimulationSetupBoundaryDefinition>(sourceDefinitions)
            : new List<SimulationSetupBoundaryDefinition>();
        DisplayName = displayName;
    }

    public IReadOnlyList<SimulationSetupBoundaryDefinition> LoadBoundaries(
        SimulationSetupModelDefinition model, out string issue)
    {
        issue = string.Empty;
        return new List<SimulationSetupBoundaryDefinition>(definitions);
    }
}

public readonly struct SimulationSetupBoundaryConfiguration
{
    public string Id { get; }
    public string Name { get; }
    public SimulationSetupBoundaryCategory Category { get; }
    public bool Enabled { get; }
    public string InputMode { get; }
    public Vector3 VelocityPhys { get; }
    public float VolumeFlowRateM3ps { get; }
    public float TemperatureDegC { get; }
    public float DischargeAngleDeg { get; }
    public bool MassFluxCorrection { get; }
    public float DensityTarget { get; }
    public float NormalVelocityBlend { get; }
    public float DensityAnchor { get; }
    public ThermalBoundaryType ThermalBoundary { get; }
    public UnityEngine.Object SourceObject { get; }

    internal SimulationSetupBoundaryConfiguration(EditableBoundaryConfiguration value)
    {
        Id = value.definition.Id;
        Name = value.definition.Name;
        Category = value.category;
        Enabled = value.enabled;
        InputMode = value.inputMode;
        VelocityPhys = value.velocityPhys;
        VolumeFlowRateM3ps = value.volumeFlowRateM3ps;
        TemperatureDegC = value.temperatureDegC;
        DischargeAngleDeg = value.dischargeAngleDeg;
        MassFluxCorrection = value.massFluxCorrection;
        DensityTarget = value.densityTarget;
        NormalVelocityBlend = value.normalVelocityBlend;
        DensityAnchor = value.densityAnchor;
        ThermalBoundary = value.thermalBoundary;
        SourceObject = value.definition.SourceObject;
    }
}

internal sealed class EditableBoundaryConfiguration
{
    public readonly SimulationSetupBoundaryDefinition definition;
    public SimulationSetupBoundaryCategory category;
    public bool enabled;
    public string inputMode;
    public Vector3 velocityPhys;
    public float volumeFlowRateM3ps;
    public float temperatureDegC;
    public float dischargeAngleDeg;
    public bool massFluxCorrection;
    public float densityTarget;
    public float normalVelocityBlend;
    public float densityAnchor;
    public ThermalBoundaryType thermalBoundary;

    public EditableBoundaryConfiguration(SimulationSetupBoundaryDefinition source)
    {
        definition = source;
        category = source.Category;
        enabled = source.Enabled;
        inputMode = source.InputMode;
        velocityPhys = source.VelocityPhys;
        volumeFlowRateM3ps = source.VolumeFlowRateM3ps;
        temperatureDegC = source.TemperatureDegC;
        dischargeAngleDeg = source.DischargeAngleDeg;
        massFluxCorrection = source.MassFluxCorrection;
        densityTarget = source.DensityTarget;
        normalVelocityBlend = source.NormalVelocityBlend;
        densityAnchor = source.DensityAnchor;
        thermalBoundary = source.ThermalBoundary;
    }

    public SimulationSetupBoundaryConfiguration Snapshot()
    {
        return new SimulationSetupBoundaryConfiguration(this);
    }
}

public sealed class SimulationSetupBoundaryController : IDisposable
{
    private const int EmptyPatchAutoRetryLimit = 12;
    private const string SelectedRowClass = "boundary-list-item--selected";
    private const string SelectedCategoryClass = "boundary-summary__card--selected";
    private static readonly List<string> InletModes = new List<string>
    {
        LBMZouHeBox.BoundaryInputMode.Velocity.ToString(),
        LBMZouHeBox.BoundaryInputMode.VolumeFlowRate.ToString()
    };
    private static readonly List<string> WallThermalModes = new List<string>
    {
        ThermalBoundaryType.Adiabatic.ToString(),
        ThermalBoundaryType.Isothermal.ToString()
    };

    private readonly List<EditableBoundaryConfiguration> configurations =
        new List<EditableBoundaryConfiguration>();
    private readonly Dictionary<string, Button> rows = new Dictionary<string, Button>();
    private readonly Dictionary<string, Action> rowHandlers = new Dictionary<string, Action>();

    private SimulationSetupModelController modelSelection;
    private ISimulationSetupBoundarySource source;
    private Button inletTab;
    private Button outletTab;
    private Button wallTab;
    private Label inletCount;
    private Label outletCount;
    private Label wallCount;
    private Label listTitle;
    private Label listCount;
    private ScrollView itemList;
    private Label selectedName;
    private Label selectedSource;
    private Label selectedType;
    private DropdownField patchTypeField;
    private Toggle enabledToggle;
    private Label gridPatchValue;
    private VisualElement inletFields;
    private DropdownField inletModeField;
    private Vector3Field inletVelocityField;
    private FloatField inletFlowRateField;
    private FloatField inletTemperatureField;
    private FloatField inletDischargeAngleField;
    private Label inletGeometryValue;
    private VisualElement outletFields;
    private Label outletModeValue;
    private Toggle massFluxToggle;
    private FloatField outletDensityField;
    private FloatField outletBlendField;
    private FloatField outletAnchorField;
    private Label outletFlowValue;
    private VisualElement wallFields;
    private Label wallMomentumValue;
    private DropdownField wallThermalField;
    private FloatField wallTemperatureField;
    private Label wallHintValue;
    private Label status;
    private SimulationSetupBoundaryCategory currentCategory = SimulationSetupBoundaryCategory.Inlet;
    private EditableBoundaryConfiguration current;
    private string loadedModelId = string.Empty;
    private bool dirty = true;
    private bool updatingUi;
    private bool isValid;
    private int emptyPatchAutoRetryCount;

    public int BoundaryCount => configurations.Count;
    public int InletCount => CountCategory(SimulationSetupBoundaryCategory.Inlet);
    public int OutletCount => CountCategory(SimulationSetupBoundaryCategory.Outlet);
    public int WallCount => CountCategory(SimulationSetupBoundaryCategory.Wall);
    public bool HasSelection => current != null;
    public bool IsValid => isValid;
    public SimulationSetupBoundaryCategory CurrentCategory => currentCategory;
    public SimulationSetupBoundaryConfiguration CurrentConfiguration =>
        current != null ? current.Snapshot() : default;

    public IReadOnlyList<SimulationSetupBoundaryConfiguration> GetStagedConfigurations()
    {
        var snapshots = new List<SimulationSetupBoundaryConfiguration>(configurations.Count);
        for (int i = 0; i < configurations.Count; i++)
            snapshots.Add(configurations[i].Snapshot());
        return snapshots;
    }

    public event Action<SimulationSetupBoundaryConfiguration> ConfigurationChanged;

    public bool Initialize(
        VisualElement root, SimulationSetupModelController models,
        ISimulationSetupBoundarySource boundarySource, out string issue)
    {
        Dispose();
        if (root == null || models == null || boundarySource == null)
        {
            issue = "Boundary UI root, Model controller, or boundary source is missing.";
            return false;
        }

        inletTab = root.Q<Button>("BoundaryInletTabButton");
        outletTab = root.Q<Button>("BoundaryOutletTabButton");
        wallTab = root.Q<Button>("BoundaryWallTabButton");
        inletCount = root.Q<Label>("BoundaryInletCountValue");
        outletCount = root.Q<Label>("BoundaryOutletCountValue");
        wallCount = root.Q<Label>("BoundaryWallCountValue");
        listTitle = root.Q<Label>("BoundaryListTitle");
        listCount = root.Q<Label>("BoundaryListCountValue");
        itemList = root.Q<ScrollView>("BoundaryItemListScrollView");
        selectedName = root.Q<Label>("SelectedBoundaryNameValue");
        selectedSource = root.Q<Label>("SelectedBoundarySourceValue");
        selectedType = root.Q<Label>("SelectedBoundaryTypeValue");
        patchTypeField = root.Q<DropdownField>("BoundaryPatchTypeField");
        enabledToggle = root.Q<Toggle>("BoundaryEnabledToggle");
        gridPatchValue = root.Q<Label>("BoundaryGridPatchValue");
        inletFields = root.Q<VisualElement>("BoundaryInletFields");
        inletModeField = root.Q<DropdownField>("BoundaryInletModeField");
        inletVelocityField = root.Q<Vector3Field>("BoundaryInletVelocityField");
        inletFlowRateField = root.Q<FloatField>("BoundaryInletFlowRateField");
        inletTemperatureField = root.Q<FloatField>("BoundaryInletTemperatureField");
        inletDischargeAngleField = root.Q<FloatField>("BoundaryInletDischargeAngleField");
        inletGeometryValue = root.Q<Label>("BoundaryInletGeometryValue");
        outletFields = root.Q<VisualElement>("BoundaryOutletFields");
        outletModeValue = root.Q<Label>("BoundaryOutletModeValue");
        massFluxToggle = root.Q<Toggle>("BoundaryMassFluxToggle");
        outletDensityField = root.Q<FloatField>("BoundaryOutletDensityField");
        outletBlendField = root.Q<FloatField>("BoundaryOutletBlendField");
        outletAnchorField = root.Q<FloatField>("BoundaryOutletAnchorField");
        outletFlowValue = root.Q<Label>("BoundaryOutletFlowValue");
        wallFields = root.Q<VisualElement>("BoundaryWallFields");
        wallMomentumValue = root.Q<Label>("BoundaryWallMomentumValue");
        wallThermalField = root.Q<DropdownField>("BoundaryWallThermalField");
        wallTemperatureField = root.Q<FloatField>("BoundaryWallTemperatureField");
        wallHintValue = root.Q<Label>("BoundaryWallHintValue");
        status = root.Q<Label>("BoundaryConfigurationStatus");

        if (inletTab == null || outletTab == null || wallTab == null ||
            inletCount == null || outletCount == null || wallCount == null ||
            listTitle == null || listCount == null || itemList == null ||
            selectedName == null || selectedSource == null || selectedType == null ||
            patchTypeField == null || enabledToggle == null || gridPatchValue == null || inletFields == null ||
            inletModeField == null || inletVelocityField == null || inletFlowRateField == null ||
            inletTemperatureField == null || inletDischargeAngleField == null ||
            inletGeometryValue == null || outletFields == null ||
            outletModeValue == null || massFluxToggle == null || outletDensityField == null ||
            outletBlendField == null || outletAnchorField == null || outletFlowValue == null ||
            wallFields == null || wallMomentumValue == null || wallThermalField == null ||
            wallTemperatureField == null || wallHintValue == null || status == null)
        {
            issue = "One or more Boundary configuration UI elements are missing.";
            Dispose();
            return false;
        }

        modelSelection = models;
        source = boundarySource;
        patchTypeField.choices = new List<string>
        {
            SimulationSetupBoundaryCategory.Inlet.ToString(),
            SimulationSetupBoundaryCategory.Outlet.ToString()
        };
        inletModeField.choices = InletModes;
        wallThermalField.choices = WallThermalModes;
        massFluxToggle.SetEnabled(false);
        massFluxToggle.tooltip = "The existing Mass-Flux Corrected Outlet setting is preserved.";
        RegisterCallbacks();
        modelSelection.SelectedModelChanged += OnModelChanged;
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
        if (modelSelection == null || source == null)
            return;

        modelSelection.RefreshIfNeeded();
        ClearRows();
        configurations.Clear();
        current = null;
        loadedModelId = modelSelection.SelectedModelId;
        if (!modelSelection.HasSelection)
        {
            dirty = false;
            ShowWaitingState();
            return;
        }

        IReadOnlyList<SimulationSetupBoundaryDefinition> loaded =
            source.LoadBoundaries(modelSelection.SelectedModel, out string issue);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (loaded != null)
        {
            for (int i = 0; i < loaded.Count; i++)
            {
                SimulationSetupBoundaryDefinition definition = loaded[i];
                if (definition != null && !string.IsNullOrWhiteSpace(definition.Id) && ids.Add(definition.Id))
                    configurations.Add(new EditableBoundaryConfiguration(definition));
            }
        }

        dirty = false;
        UpdateCounts();
        if (!string.IsNullOrEmpty(issue))
        {
            BuildList();
            ClearSelection();
            SetStatus(issue, true);
            return;
        }

        if (configurations.Count == 0)
        {
            BuildList();
            ClearSelection();
            SetStatus("No supported boundary components were found in the selected model scene.", true);
            return;
        }

        if (InletCount == 0 && OutletCount == 0 &&
            emptyPatchAutoRetryCount < EmptyPatchAutoRetryLimit)
        {
            // SimulationController.Start populates its scene cache after additive
            // scene integration. Retry briefly instead of freezing the initial 0/0 scan.
            emptyPatchAutoRetryCount++;
            dirty = true;
        }
        else if (InletCount > 0 || OutletCount > 0)
        {
            emptyPatchAutoRetryCount = 0;
        }

        if (CountCategory(currentCategory) == 0)
            currentCategory = FirstAvailableCategory();
        BuildList();
        SelectFirstVisible();
        ValidateAndShowStatus(false);
    }

    public bool SelectCategory(SimulationSetupBoundaryCategory category)
    {
        if (category != SimulationSetupBoundaryCategory.Wall &&
            InletCount == 0 && OutletCount == 0)
        {
            currentCategory = category;
            emptyPatchAutoRetryCount = 0;
            dirty = true;
            Refresh();
            return CountCategory(category) > 0;
        }

        currentCategory = category;
        UpdateCategoryButtons();
        BuildList();
        SelectFirstVisible();
        ValidateAndShowStatus(false);
        return CountCategory(category) > 0;
    }

    public bool SelectBoundary(string id)
    {
        EditableBoundaryConfiguration match = configurations.Find(item => item.definition.Id == id);
        if (match == null)
            return false;

        currentCategory = match.category;
        current = match;
        UpdateCategoryButtons();
        BuildList();
        foreach (KeyValuePair<string, Button> row in rows)
            row.Value.EnableInClassList(SelectedRowClass, row.Key == id);
        PushCurrentToUi();
        ValidateAndShowStatus(false);
        return true;
    }

    public void Dispose()
    {
        UnregisterCallbacks();
        if (modelSelection != null)
            modelSelection.SelectedModelChanged -= OnModelChanged;
        ClearRows();
        configurations.Clear();
        modelSelection = null;
        source = null;
        inletTab = null;
        outletTab = null;
        wallTab = null;
        inletCount = null;
        outletCount = null;
        wallCount = null;
        listTitle = null;
        listCount = null;
        itemList = null;
        selectedName = null;
        selectedSource = null;
        selectedType = null;
        patchTypeField = null;
        enabledToggle = null;
        gridPatchValue = null;
        inletFields = null;
        inletModeField = null;
        inletVelocityField = null;
        inletFlowRateField = null;
        inletTemperatureField = null;
        inletDischargeAngleField = null;
        inletGeometryValue = null;
        outletFields = null;
        outletModeValue = null;
        massFluxToggle = null;
        outletDensityField = null;
        outletBlendField = null;
        outletAnchorField = null;
        outletFlowValue = null;
        wallFields = null;
        wallMomentumValue = null;
        wallThermalField = null;
        wallTemperatureField = null;
        wallHintValue = null;
        status = null;
        current = null;
        loadedModelId = string.Empty;
        currentCategory = SimulationSetupBoundaryCategory.Inlet;
        dirty = true;
        updatingUi = false;
        isValid = false;
        emptyPatchAutoRetryCount = 0;
        ConfigurationChanged = null;
    }

    private void RegisterCallbacks()
    {
        inletTab.clicked += OnInletTabClicked;
        outletTab.clicked += OnOutletTabClicked;
        wallTab.clicked += OnWallTabClicked;
        patchTypeField.RegisterValueChangedCallback(OnPatchTypeChanged);
        enabledToggle.RegisterValueChangedCallback(OnEnabledChanged);
        inletModeField.RegisterValueChangedCallback(OnInletModeChanged);
        inletVelocityField.RegisterValueChangedCallback(OnInletVelocityChanged);
        inletFlowRateField.RegisterValueChangedCallback(OnInletFlowRateChanged);
        inletTemperatureField.RegisterValueChangedCallback(OnTemperatureChanged);
        inletDischargeAngleField.RegisterValueChangedCallback(OnDischargeAngleChanged);
        outletDensityField.RegisterValueChangedCallback(OnOutletDensityChanged);
        outletBlendField.RegisterValueChangedCallback(OnOutletBlendChanged);
        outletAnchorField.RegisterValueChangedCallback(OnOutletAnchorChanged);
        wallThermalField.RegisterValueChangedCallback(OnWallThermalChanged);
        wallTemperatureField.RegisterValueChangedCallback(OnTemperatureChanged);
    }

    private void UnregisterCallbacks()
    {
        if (inletTab != null) inletTab.clicked -= OnInletTabClicked;
        if (outletTab != null) outletTab.clicked -= OnOutletTabClicked;
        if (wallTab != null) wallTab.clicked -= OnWallTabClicked;
        patchTypeField?.UnregisterValueChangedCallback(OnPatchTypeChanged);
        enabledToggle?.UnregisterValueChangedCallback(OnEnabledChanged);
        inletModeField?.UnregisterValueChangedCallback(OnInletModeChanged);
        inletVelocityField?.UnregisterValueChangedCallback(OnInletVelocityChanged);
        inletFlowRateField?.UnregisterValueChangedCallback(OnInletFlowRateChanged);
        inletTemperatureField?.UnregisterValueChangedCallback(OnTemperatureChanged);
        inletDischargeAngleField?.UnregisterValueChangedCallback(OnDischargeAngleChanged);
        outletDensityField?.UnregisterValueChangedCallback(OnOutletDensityChanged);
        outletBlendField?.UnregisterValueChangedCallback(OnOutletBlendChanged);
        outletAnchorField?.UnregisterValueChangedCallback(OnOutletAnchorChanged);
        wallThermalField?.UnregisterValueChangedCallback(OnWallThermalChanged);
        wallTemperatureField?.UnregisterValueChangedCallback(OnTemperatureChanged);
    }

    private void OnInletTabClicked() => SelectCategory(SimulationSetupBoundaryCategory.Inlet);
    private void OnOutletTabClicked() => SelectCategory(SimulationSetupBoundaryCategory.Outlet);
    private void OnWallTabClicked() => SelectCategory(SimulationSetupBoundaryCategory.Wall);

    private void OnPatchTypeChanged(ChangeEvent<string> evt)
    {
        if (!CanEdit() || !(current.definition.SourceObject is LBMZouHeBox))
            return;
        if (!Enum.TryParse(evt.newValue, out SimulationSetupBoundaryCategory category) ||
            category == SimulationSetupBoundaryCategory.Wall || category == current.category)
            return;

        current.category = category;
        if (category == SimulationSetupBoundaryCategory.Outlet)
        {
            current.inputMode = LBMZouHeBox.BoundaryInputMode.AutoMassBalancedOutlet.ToString();
            current.massFluxCorrection = true;
        }
        else if (current.inputMode == LBMZouHeBox.BoundaryInputMode.AutoMassBalancedOutlet.ToString() ||
                 current.inputMode == LBMZouHeBox.BoundaryInputMode.PressureDensity.ToString())
        {
            current.inputMode = LBMZouHeBox.BoundaryInputMode.Velocity.ToString();
        }

        currentCategory = category;
        UpdateCounts();
        BuildList();
        foreach (KeyValuePair<string, Button> row in rows)
            row.Value.EnableInClassList(SelectedRowClass, row.Key == current.definition.Id);
        PushCurrentToUi();
        CommitEdit();
    }

    private void OnModelChanged(SimulationSetupModelDefinition model)
    {
        if (model.Id != loadedModelId)
            MarkDirty();
    }

    private bool CanEdit()
    {
        return !updatingUi && current != null;
    }

    private void OnEnabledChanged(ChangeEvent<bool> evt)
    {
        if (!CanEdit() || current.definition.IsImplicitDomainWall) return;
        current.enabled = evt.newValue;
        CommitEdit();
    }

    private void OnInletModeChanged(ChangeEvent<string> evt)
    {
        if (!CanEdit() || current.category != SimulationSetupBoundaryCategory.Inlet) return;
        current.inputMode = InletModes.Contains(evt.newValue) ? evt.newValue : InletModes[0];
        UpdateConditionalControls();
        CommitEdit();
    }

    private void OnInletVelocityChanged(ChangeEvent<Vector3> evt)
    {
        if (!CanEdit()) return;
        current.velocityPhys = evt.newValue;
        if (current.definition.SourceObject is LBMZouHeBox patch)
        {
            current.dischargeAngleDeg = patch.CalculateDischargeAngleDeg(evt.newValue);
            inletDischargeAngleField.SetValueWithoutNotify(current.dischargeAngleDeg);
        }
        CommitEdit();
    }

    private void OnInletFlowRateChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.volumeFlowRateM3ps = Mathf.Max(0f, evt.newValue);
        inletFlowRateField.SetValueWithoutNotify(current.volumeFlowRateM3ps);
        CommitEdit();
    }

    private void OnTemperatureChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.temperatureDegC = evt.newValue;
        CommitEdit();
    }

    private void OnDischargeAngleChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit() || current.category != SimulationSetupBoundaryCategory.Inlet)
            return;

        current.dischargeAngleDeg = Mathf.Clamp(evt.newValue, 0f, 90f);
        inletDischargeAngleField.SetValueWithoutNotify(current.dischargeAngleDeg);
        CommitEdit();
    }

    private void OnOutletDensityChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.densityTarget = evt.newValue;
        CommitEdit();
    }

    private void OnOutletBlendChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.normalVelocityBlend = Mathf.Clamp01(evt.newValue);
        outletBlendField.SetValueWithoutNotify(current.normalVelocityBlend);
        CommitEdit();
    }

    private void OnOutletAnchorChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.densityAnchor = Mathf.Clamp01(evt.newValue);
        outletAnchorField.SetValueWithoutNotify(current.densityAnchor);
        CommitEdit();
    }

    private void OnWallThermalChanged(ChangeEvent<string> evt)
    {
        if (!CanEdit() || current.definition.IsImplicitDomainWall) return;
        if (!Enum.TryParse(evt.newValue, out ThermalBoundaryType parsed))
            parsed = ThermalBoundaryType.Adiabatic;
        current.thermalBoundary = parsed;
        UpdateConditionalControls();
        CommitEdit();
    }

    private void CommitEdit()
    {
        ValidateAndShowStatus(true);
        ConfigurationChanged?.Invoke(current.Snapshot());
    }

    private void UpdateCounts()
    {
        inletCount.text = InletCount.ToString(CultureInfo.InvariantCulture);
        outletCount.text = OutletCount.ToString(CultureInfo.InvariantCulture);
        wallCount.text = WallCount.ToString(CultureInfo.InvariantCulture);
    }

    private int CountCategory(SimulationSetupBoundaryCategory category)
    {
        int count = 0;
        for (int i = 0; i < configurations.Count; i++)
            if (configurations[i].category == category) count++;
        return count;
    }

    private SimulationSetupBoundaryCategory FirstAvailableCategory()
    {
        if (InletCount > 0) return SimulationSetupBoundaryCategory.Inlet;
        if (OutletCount > 0) return SimulationSetupBoundaryCategory.Outlet;
        return SimulationSetupBoundaryCategory.Wall;
    }

    private void UpdateCategoryButtons()
    {
        inletTab.EnableInClassList(SelectedCategoryClass,
            currentCategory == SimulationSetupBoundaryCategory.Inlet);
        outletTab.EnableInClassList(SelectedCategoryClass,
            currentCategory == SimulationSetupBoundaryCategory.Outlet);
        wallTab.EnableInClassList(SelectedCategoryClass,
            currentCategory == SimulationSetupBoundaryCategory.Wall);
        listTitle.text = currentCategory == SimulationSetupBoundaryCategory.Inlet
            ? "Inlet patches"
            : currentCategory == SimulationSetupBoundaryCategory.Outlet
                ? "Outlet patches"
                : "Wall boundaries";
        int count = CountCategory(currentCategory);
        listCount.text = count == 1 ? "1 item" : $"{count} items";
    }

    private void BuildList()
    {
        ClearRows();
        UpdateCategoryButtons();
        for (int i = 0; i < configurations.Count; i++)
        {
            EditableBoundaryConfiguration configuration = configurations[i];
            if (configuration.category != currentCategory)
                continue;

            var row = new Button { name = "BoundaryListItem_" + SanitizeName(configuration.definition.Id) };
            row.AddToClassList("boundary-list-item");
            var name = new Label(configuration.definition.Name);
            name.AddToClassList("boundary-list-item__name");
            row.Add(name);
            var summary = new Label(BuildSummary(configuration));
            summary.AddToClassList("boundary-list-item__summary");
            row.Add(summary);
            string id = configuration.definition.Id;
            Action handler = () => SelectBoundary(id);
            row.clicked += handler;
            rows[id] = row;
            rowHandlers[id] = handler;
            itemList.Add(row);
        }
    }

    private static string BuildSummary(EditableBoundaryConfiguration configuration)
    {
        if (configuration.category == SimulationSetupBoundaryCategory.Inlet)
            return $"{configuration.inputMode} | {configuration.definition.Normal}";
        if (configuration.category == SimulationSetupBoundaryCategory.Outlet)
            return configuration.massFluxCorrection ? "Auto mass-balanced outlet" : "Pressure density outlet";
        return configuration.definition.IsImplicitDomainWall
            ? "Implicit domain shell"
            : $"Solid | {configuration.thermalBoundary}";
    }

    private void SelectFirstVisible()
    {
        for (int i = 0; i < configurations.Count; i++)
        {
            if (configurations[i].category == currentCategory)
            {
                current = configurations[i];
                foreach (KeyValuePair<string, Button> row in rows)
                    row.Value.EnableInClassList(SelectedRowClass, row.Key == current.definition.Id);
                PushCurrentToUi();
                return;
            }
        }

        ClearSelection();
    }

    private void PushCurrentToUi()
    {
        if (current == null)
        {
            ClearSelection();
            return;
        }

        updatingUi = true;
        SimulationSetupBoundaryDefinition definition = current.definition;
        selectedName.text = definition.Name;
        selectedSource.text = definition.SourceType;
        selectedType.text = current.category.ToString();
        patchTypeField.SetValueWithoutNotify(current.category.ToString());
        patchTypeField.SetEnabled(definition.SourceObject is LBMZouHeBox);
        enabledToggle.SetValueWithoutNotify(current.enabled);
        enabledToggle.SetEnabled(!definition.IsImplicitDomainWall);
        gridPatchValue.text = string.IsNullOrEmpty(definition.GridPatch) ? "Pending solver indexing" : definition.GridPatch;

        inletFields.style.display = current.category == SimulationSetupBoundaryCategory.Inlet
            ? DisplayStyle.Flex : DisplayStyle.None;
        outletFields.style.display = current.category == SimulationSetupBoundaryCategory.Outlet
            ? DisplayStyle.Flex : DisplayStyle.None;
        wallFields.style.display = current.category == SimulationSetupBoundaryCategory.Wall
            ? DisplayStyle.Flex : DisplayStyle.None;

        inletModeField.SetValueWithoutNotify(current.inputMode);
        inletVelocityField.SetValueWithoutNotify(current.velocityPhys);
        inletFlowRateField.SetValueWithoutNotify(current.volumeFlowRateM3ps);
        inletTemperatureField.SetValueWithoutNotify(current.temperatureDegC);
        inletDischargeAngleField.SetValueWithoutNotify(current.dischargeAngleDeg);
        inletGeometryValue.text = $"{definition.Normal} / {definition.AreaM2:0.####} m2";
        outletModeValue.text = current.inputMode;
        massFluxToggle.SetValueWithoutNotify(current.massFluxCorrection);
        outletDensityField.SetValueWithoutNotify(current.densityTarget);
        outletBlendField.SetValueWithoutNotify(current.normalVelocityBlend);
        outletAnchorField.SetValueWithoutNotify(current.densityAnchor);
        outletFlowValue.text = $"{definition.TargetFlowRateM3ps:0.####} m3/s";
        wallMomentumValue.text = definition.MomentumBoundary;
        wallThermalField.SetValueWithoutNotify(current.thermalBoundary.ToString());
        wallTemperatureField.SetValueWithoutNotify(current.temperatureDegC);
        wallHintValue.text = definition.IsImplicitDomainWall
            ? "Domain shell type is fixed by D3Q7LBMThermalKernel.compute and cannot be edited."
            : "Thermal type maps directly to DeviceObstacles; geometry remains unchanged.";
        UpdateConditionalControls();
        updatingUi = false;
    }

    private void UpdateConditionalControls()
    {
        if (current == null) return;
        bool velocityMode = current.inputMode == LBMZouHeBox.BoundaryInputMode.Velocity.ToString();
        inletVelocityField.SetEnabled(velocityMode);
        inletFlowRateField.SetEnabled(!velocityMode);
        bool editableWall = current.category == SimulationSetupBoundaryCategory.Wall &&
                            !current.definition.IsImplicitDomainWall;
        wallThermalField.SetEnabled(editableWall);
        wallTemperatureField.SetEnabled(editableWall &&
            current.thermalBoundary == ThermalBoundaryType.Isothermal);
    }

    private void ClearSelection()
    {
        current = null;
        selectedName.text = "No boundary selected";
        selectedSource.text = "Choose a category containing a boundary.";
        selectedType.text = "Waiting";
        patchTypeField.SetValueWithoutNotify(string.Empty);
        patchTypeField.SetEnabled(false);
        enabledToggle.SetEnabled(false);
        gridPatchValue.text = "-";
        inletFields.style.display = DisplayStyle.None;
        outletFields.style.display = DisplayStyle.None;
        wallFields.style.display = DisplayStyle.None;
    }

    private void ClearRows()
    {
        if (itemList != null)
        {
            foreach (KeyValuePair<string, Button> row in rows)
            {
                if (rowHandlers.TryGetValue(row.Key, out Action handler))
                    row.Value.clicked -= handler;
            }
            itemList.Clear();
        }
        rows.Clear();
        rowHandlers.Clear();
    }

    private void ShowWaitingState()
    {
        if (inletCount == null) return;
        UpdateCounts();
        BuildList();
        ClearSelection();
        SetStatus("Select a loaded model. Boundary components are scanned only when this step opens.", false);
    }

    private void ValidateAndShowStatus(bool edited)
    {
        isValid = TryValidate(out string issue);
        if (!isValid)
        {
            SetStatus(issue, true);
            return;
        }

        string prefix = edited ? "Staged change validated. " : string.Empty;
        SetStatus(
            $"{prefix}{InletCount} inlet, {OutletCount} outlet, {WallCount} wall boundaries from " +
            $"{source.DisplayName}. Existing solver definitions are preserved; changes apply at Run.", false);
    }

    private bool TryValidate(out string issue)
    {
        if (configurations.Count == 0)
        {
            issue = "No supported boundaries are available for validation.";
            return false;
        }
        if (InletCount == 0 || OutletCount == 0)
        {
            issue = "The selected solver model requires at least one inlet and one outlet patch.";
            return false;
        }

        int activeInlets = 0;
        int activeOutlets = 0;
        for (int i = 0; i < configurations.Count; i++)
        {
            EditableBoundaryConfiguration item = configurations[i];
            if (!item.enabled && !item.definition.IsImplicitDomainWall)
                continue;
            if (item.category == SimulationSetupBoundaryCategory.Inlet)
            {
                activeInlets++;
                if (!InletModes.Contains(item.inputMode) || !IsFinite(item.temperatureDegC) ||
                    !IsFinite(item.dischargeAngleDeg) || item.dischargeAngleDeg < 0f ||
                    item.dischargeAngleDeg > 90f ||
                    (item.inputMode == InletModes[0] && !IsFinite(item.velocityPhys)) ||
                    (item.inputMode == InletModes[1] &&
                     (!IsFinite(item.volumeFlowRateM3ps) || item.volumeFlowRateM3ps <= 0f)))
                {
                    issue = $"Inlet '{item.definition.Name}' has invalid velocity, flow, temperature, or discharge angle data.";
                    return false;
                }
            }
            else if (item.category == SimulationSetupBoundaryCategory.Outlet)
            {
                activeOutlets++;
                if (!item.massFluxCorrection)
                {
                    issue = $"Outlet '{item.definition.Name}' must retain Mass-Flux Corrected Outlet.";
                    return false;
                }
                if (!IsFinite(item.densityTarget) || item.densityTarget <= 0f ||
                    !IsUnitInterval(item.normalVelocityBlend) || !IsUnitInterval(item.densityAnchor))
                {
                    issue = $"Outlet '{item.definition.Name}' has invalid density, blend, or anchor data.";
                    return false;
                }
            }
            else if (item.thermalBoundary == ThermalBoundaryType.Isothermal &&
                     !IsFinite(item.temperatureDegC))
            {
                issue = $"Wall '{item.definition.Name}' has an invalid isothermal temperature.";
                return false;
            }
        }

        if (activeInlets == 0 || activeOutlets == 0)
        {
            issue = "At least one enabled inlet and outlet are required.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private void SetStatus(string message, bool error)
    {
        if (status == null) return;
        status.text = message;
        status.EnableInClassList("boundary-status--error", error);
    }

    private static bool IsUnitInterval(float value)
    {
        return IsFinite(value) && value >= 0f && value <= 1f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "Boundary";
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_') chars[i] = '_';
        return new string(chars);
    }
}
