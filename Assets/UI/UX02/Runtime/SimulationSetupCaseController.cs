using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

public readonly struct SimulationSetupInitialConditions
{
    public float IndoorTemperatureDegC { get; }
    public float IndoorHumidityPercent { get; }
    public float OutdoorTemperatureDegC { get; }
    public float OutdoorHumidityPercent { get; }
    public float SetTemperatureDegC { get; }
    public float TargetSimulationTimeSeconds { get; }

    public SimulationSetupInitialConditions(
        float indoorTemperatureDegC, float indoorHumidityPercent,
        float outdoorTemperatureDegC, float outdoorHumidityPercent,
        float setTemperatureDegC, float targetSimulationTimeSeconds)
    {
        IndoorTemperatureDegC = indoorTemperatureDegC;
        IndoorHumidityPercent = indoorHumidityPercent;
        OutdoorTemperatureDegC = outdoorTemperatureDegC;
        OutdoorHumidityPercent = outdoorHumidityPercent;
        SetTemperatureDegC = setTemperatureDegC;
        TargetSimulationTimeSeconds = targetSimulationTimeSeconds;
    }

    public static SimulationSetupInitialConditions Defaults =>
        new SimulationSetupInitialConditions(30f, 50f, 35f, 70f, 28f, 30f);
}

internal sealed class EditableSimulationSetupInitialConditions
{
    public float indoorTemperatureDegC = 30f;
    public float indoorHumidityPercent = 50f;
    public float outdoorTemperatureDegC = 35f;
    public float outdoorHumidityPercent = 70f;
    public float setTemperatureDegC = 28f;
    public float targetSimulationTimeSeconds = 30f;

    public EditableSimulationSetupInitialConditions Clone() =>
        new EditableSimulationSetupInitialConditions
        {
            indoorTemperatureDegC = indoorTemperatureDegC,
            indoorHumidityPercent = indoorHumidityPercent,
            outdoorTemperatureDegC = outdoorTemperatureDegC,
            outdoorHumidityPercent = outdoorHumidityPercent,
            setTemperatureDegC = setTemperatureDegC,
            targetSimulationTimeSeconds = targetSimulationTimeSeconds
        };

    public SimulationSetupInitialConditions Snapshot() =>
        new SimulationSetupInitialConditions(
            indoorTemperatureDegC, indoorHumidityPercent,
            outdoorTemperatureDegC, outdoorHumidityPercent,
            setTemperatureDegC, targetSimulationTimeSeconds);
}

public readonly struct SimulationSetupCaseDefinition
{
    public readonly string Id;
    public readonly CaseStudyPreset BasedOnPreset;
    public readonly string Name;
    public readonly string Group;
    public readonly string Description;
    public readonly float DxPhys;
    public readonly float TauFluidMin;
    public readonly float TauThermalMin;
    public readonly string Turbulence;
    public readonly bool IsBuiltIn;
    public readonly string CreatedUtc;

    public SimulationSetupCaseDefinition(
        CaseStudyPreset preset,
        string group,
        string description,
        float tauFluidMin,
        float tauThermalMin,
        string turbulence)
    {
        Id = "builtin:" + preset;
        BasedOnPreset = preset;
        Name = preset.ToString();
        Group = group;
        Description = description;
        DxPhys = 0.04f;
        TauFluidMin = tauFluidMin;
        TauThermalMin = tauThermalMin;
        Turbulence = turbulence;
        IsBuiltIn = true;
        CreatedUtc = string.Empty;
    }

    public SimulationSetupCaseDefinition(SimulationSetupUserCase userCase)
    {
        Id = userCase.id;
        BasedOnPreset = ParsePreset(userCase.basedOnPreset);
        Name = userCase.name;
        Group = "User";
        Description = userCase.description;
        DxPhys = userCase.dxPhys;
        TauFluidMin = userCase.tauFluidMin;
        TauThermalMin = userCase.tauThermalMin;
        Turbulence = userCase.turbulence;
        IsBuiltIn = false;
        CreatedUtc = userCase.createdUtc;
    }

    private static CaseStudyPreset ParsePreset(string value)
    {
        return Enum.TryParse(value, out CaseStudyPreset preset)
            ? preset
            : CaseStudyPreset.A0_Baseline;
    }
}

public static class SimulationSetupCaseCatalog
{
    private static readonly SimulationSetupCaseDefinition[] Definitions =
    {
        new SimulationSetupCaseDefinition(CaseStudyPreset.A0_Baseline, "Primary",
            "Current conservative clamp baseline.", 0.560f, 0.560f, "Smagorinsky (0.03)"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.A1_FluidTau_0530_Thermal_0560_Off, "Primary",
            "Fluid viscosity diffusion reduction only; thermal diffusion kept at baseline.", 0.530f, 0.560f, "Off"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.A2_FluidTau_0510_Thermal_0560_Off, "Primary",
            "Aggressive fluid viscosity reduction; thermal diffusion kept at baseline.", 0.510f, 0.560f, "Off"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.A3_FluidTau_0510_Thermal_0530_Off, "Primary",
            "Fluid and thermal diffusion both reduced without turbulence model.", 0.510f, 0.530f, "Off"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.A4_FluidTau_0510_Thermal_0530_Smag003, "Primary",
            "Reduced fluid/thermal diffusion with light Smagorinsky stabilization.", 0.510f, 0.530f, "Smagorinsky (0.03)"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.B1_FluidTau_0515_Thermal_0535_Off, "Secondary",
            "Secondary sweep with moderate fluid and thermal diffusion reduction.", 0.515f, 0.535f, "Off"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.B2_FluidTau_0510_Thermal_0530_Off, "Secondary",
            "Secondary sweep reference without a turbulence model.", 0.510f, 0.530f, "Off"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.B3_FluidTau_0505_Thermal_0525_Off, "Secondary",
            "Secondary sweep near the fluid relaxation stability limit.", 0.505f, 0.525f, "Off"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.B4_FluidTau_0510_Thermal_0530_Smag003, "Secondary",
            "Secondary sweep with Smagorinsky Cs=0.03.", 0.510f, 0.530f, "Smagorinsky (0.03)"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.B5_FluidTau_0510_Thermal_0530_Smag006, "Secondary",
            "Secondary sweep with Smagorinsky Cs=0.06.", 0.510f, 0.530f, "Smagorinsky (0.06)"),
        new SimulationSetupCaseDefinition(CaseStudyPreset.B6_FluidTau_0510_Thermal_0530_Smag010, "Secondary",
            "Secondary sweep with Smagorinsky Cs=0.10.", 0.510f, 0.530f, "Smagorinsky (0.10)")
    };

    public static IReadOnlyList<SimulationSetupCaseDefinition> All => Definitions;

    public static bool TryGet(CaseStudyPreset preset, out SimulationSetupCaseDefinition definition)
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            if (Definitions[i].BasedOnPreset != preset)
                continue;

            definition = Definitions[i];
            return true;
        }

        definition = default;
        return false;
    }
}

public sealed class SimulationSetupCaseController : IDisposable
{
    private const string SelectedClassName = "case-list-item--selected";
    private readonly Dictionary<string, Button> caseButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> clickHandlers = new Dictionary<string, Action>(StringComparer.Ordinal);
    private readonly List<SimulationSetupCaseDefinition> definitions = new List<SimulationSetupCaseDefinition>();
    private readonly List<SimulationSetupUserCase> userCases = new List<SimulationSetupUserCase>();
    private readonly Dictionary<string, EditableSimulationSetupInitialConditions> initialConditions =
        new Dictionary<string, EditableSimulationSetupInitialConditions>(StringComparer.Ordinal);

    private ISimulationSetupCaseStore caseStore;
    private ScrollView caseList;
    private Label caseCountValue;
    private Label headerCaseValue;
    private Label selectedSourceValue;
    private Label selectedNameValue;
    private Label selectedDescriptionValue;
    private Label selectedDxValue;
    private Label selectedFluidTauValue;
    private Label selectedThermalTauValue;
    private Label selectedTurbulenceValue;
    private Label selectedStorageValue;
    private Label selectedCreatedValue;
    private Label selectionStatus;
    private FloatField indoorTemperatureField;
    private FloatField indoorHumidityField;
    private FloatField outdoorTemperatureField;
    private FloatField outdoorHumidityField;
    private FloatField setTemperatureField;
    private FloatField targetSimulationTimeField;
    private Button newCaseButton;
    private Button duplicateCaseButton;
    private Button deleteCaseButton;
    private SimulationSetupCaseDefinition selectedDefinition;

    public int CaseCount => definitions.Count;
    public CaseStudyPreset SelectedCase => selectedDefinition.BasedOnPreset;
    public string SelectedCaseId => selectedDefinition.Id;
    public SimulationSetupCaseDefinition SelectedDefinition => selectedDefinition;
    public SimulationSetupInitialConditions SelectedInitialConditions =>
        GetSelectedInitialConditions().Snapshot();
    public bool InitialConditionsValid => TryValidateInitialConditions(
        GetSelectedInitialConditions(), out _);
    public bool SupportsPersistentEditing => caseStore != null;
    public bool CanDeleteSelectedCase => !string.IsNullOrEmpty(selectedDefinition.Id) && !selectedDefinition.IsBuiltIn;

    public event Action<SimulationSetupCaseDefinition> SelectedCaseChanged;

    public bool Initialize(VisualElement documentRoot, CaseStudyPreset initialCase,
        ISimulationSetupCaseStore store, out string issue)
    {
        Dispose();
        if (documentRoot == null || store == null)
        {
            issue = documentRoot == null ? "UIDocument root is missing." : "Case store is missing.";
            return false;
        }

        caseList = documentRoot.Q<ScrollView>("CaseListScrollView");
        caseCountValue = documentRoot.Q<Label>("CaseCountValue");
        headerCaseValue = documentRoot.Q<Label>("SimulationSetupCaseValue");
        selectedSourceValue = documentRoot.Q<Label>("SelectedCaseSourceValue");
        selectedNameValue = documentRoot.Q<Label>("SelectedCaseNameValue");
        selectedDescriptionValue = documentRoot.Q<Label>("SelectedCaseDescriptionValue");
        selectedDxValue = documentRoot.Q<Label>("SelectedCaseDxValue");
        selectedFluidTauValue = documentRoot.Q<Label>("SelectedCaseFluidTauValue");
        selectedThermalTauValue = documentRoot.Q<Label>("SelectedCaseThermalTauValue");
        selectedTurbulenceValue = documentRoot.Q<Label>("SelectedCaseTurbulenceValue");
        selectedStorageValue = documentRoot.Q<Label>("SelectedCaseStorageValue");
        selectedCreatedValue = documentRoot.Q<Label>("SelectedCaseCreatedValue");
        selectionStatus = documentRoot.Q<Label>("CaseSelectionStatus");
        indoorTemperatureField = documentRoot.Q<FloatField>("CaseIndoorTemperatureField");
        indoorHumidityField = documentRoot.Q<FloatField>("CaseIndoorHumidityField");
        outdoorTemperatureField = documentRoot.Q<FloatField>("CaseOutdoorTemperatureField");
        outdoorHumidityField = documentRoot.Q<FloatField>("CaseOutdoorHumidityField");
        setTemperatureField = documentRoot.Q<FloatField>("CaseSetTemperatureField");
        targetSimulationTimeField = documentRoot.Q<FloatField>("CaseTargetSimulationTimeField");
        newCaseButton = documentRoot.Q<Button>("NewCaseButton");
        duplicateCaseButton = documentRoot.Q<Button>("DuplicateCaseButton");
        deleteCaseButton = documentRoot.Q<Button>("DeleteCaseButton");

        if (caseList == null || caseCountValue == null || headerCaseValue == null ||
            selectedSourceValue == null || selectedNameValue == null || selectedDescriptionValue == null ||
            selectedDxValue == null || selectedFluidTauValue == null || selectedThermalTauValue == null ||
            selectedTurbulenceValue == null || selectedStorageValue == null || selectedCreatedValue == null ||
            selectionStatus == null || indoorTemperatureField == null || indoorHumidityField == null ||
            outdoorTemperatureField == null || outdoorHumidityField == null || setTemperatureField == null ||
            targetSimulationTimeField == null || newCaseButton == null ||
            duplicateCaseButton == null || deleteCaseButton == null)
        {
            issue = "Case Selection UI is incomplete.";
            Dispose();
            return false;
        }

        caseStore = store;
        List<SimulationSetupUserCase> loadedCases = caseStore.Load(out string loadIssue);
        if (loadedCases != null)
            userCases.AddRange(loadedCases);

        newCaseButton.clicked += CreateNewCase;
        duplicateCaseButton.clicked += DuplicateSelectedCase;
        deleteCaseButton.clicked += DeleteSelectedCase;
        indoorTemperatureField.RegisterValueChangedCallback(OnIndoorTemperatureChanged);
        indoorHumidityField.RegisterValueChangedCallback(OnIndoorHumidityChanged);
        outdoorTemperatureField.RegisterValueChangedCallback(OnOutdoorTemperatureChanged);
        outdoorHumidityField.RegisterValueChangedCallback(OnOutdoorHumidityChanged);
        setTemperatureField.RegisterValueChangedCallback(OnSetTemperatureChanged);
        targetSimulationTimeField.RegisterValueChangedCallback(OnTargetSimulationTimeChanged);
        newCaseButton.SetEnabled(true);
        duplicateCaseButton.SetEnabled(true);
        newCaseButton.tooltip = "Create a new JSON case from A0_Baseline.";
        duplicateCaseButton.tooltip = "Duplicate the selected case as a JSON user case.";

        RebuildDefinitionsAndList();
        if (!SelectCase("builtin:" + initialCase, false))
            SelectCase("builtin:" + CaseStudyPreset.A0_Baseline, false);

        ShowStatus(string.IsNullOrEmpty(loadIssue) ? "JSON case storage is ready." : loadIssue,
            !string.IsNullOrEmpty(loadIssue));
        issue = string.Empty;
        return true;
    }

    public bool SelectCase(CaseStudyPreset preset) => SelectCase("builtin:" + preset, true);
    public bool SelectCase(string caseId) => SelectCase(caseId, true);

    public void CreateNewCase()
    {
        if (!SimulationSetupCaseCatalog.TryGet(CaseStudyPreset.A0_Baseline, out SimulationSetupCaseDefinition baseline))
        {
            ShowStatus("A0 baseline is unavailable; a new case was not created.", true);
            return;
        }

        SimulationSetupUserCase userCase = CreateUserCase(baseline, GenerateUniqueName("Custom_Case"),
            "New case created from A0_Baseline.", DateTime.UtcNow);
        userCases.Add(userCase);
        initialConditions[userCase.id] = GetInitialConditions(baseline.Id).Clone();
        if (!TrySaveUserCases(out string saveIssue))
        {
            userCases.RemoveAt(userCases.Count - 1);
            initialConditions.Remove(userCase.id);
            ShowStatus(saveIssue, true);
            return;
        }

        RebuildDefinitionsAndList();
        SelectCase(userCase.id, false);
        ShowStatus($"Created and saved '{userCase.name}'.", false);
    }

    public void DuplicateSelectedCase()
    {
        if (string.IsNullOrEmpty(selectedDefinition.Id))
            return;

        SimulationSetupUserCase duplicate = CreateUserCase(selectedDefinition,
            GenerateUniqueName(selectedDefinition.Name + "_Copy"), selectedDefinition.Description, DateTime.UtcNow);
        userCases.Add(duplicate);
        initialConditions[duplicate.id] = GetSelectedInitialConditions().Clone();
        if (!TrySaveUserCases(out string saveIssue))
        {
            userCases.RemoveAt(userCases.Count - 1);
            initialConditions.Remove(duplicate.id);
            ShowStatus(saveIssue, true);
            return;
        }

        RebuildDefinitionsAndList();
        SelectCase(duplicate.id, false);
        ShowStatus($"Duplicated and saved '{duplicate.name}'.", false);
    }

    public void DeleteSelectedCase()
    {
        if (!CanDeleteSelectedCase)
        {
            ShowStatus("Built-in cases are read-only and cannot be deleted.", true);
            return;
        }

        int index = FindUserCaseIndex(selectedDefinition.Id);
        if (index < 0)
        {
            ShowStatus("Selected user case was not found in JSON storage.", true);
            return;
        }

        SimulationSetupUserCase removed = userCases[index];
        EditableSimulationSetupInitialConditions removedConditions = GetInitialConditions(removed.id).Clone();
        userCases.RemoveAt(index);
        initialConditions.Remove(removed.id);
        if (!TrySaveUserCases(out string saveIssue))
        {
            userCases.Insert(index, removed);
            initialConditions[removed.id] = removedConditions;
            ShowStatus(saveIssue, true);
            return;
        }

        RebuildDefinitionsAndList();
        SelectCase("builtin:" + CaseStudyPreset.A0_Baseline, false);
        ShowStatus($"Deleted '{removed.name}' from JSON storage.", false);
    }

    public void Dispose()
    {
        ClearCaseButtons();
        if (newCaseButton != null) newCaseButton.clicked -= CreateNewCase;
        if (duplicateCaseButton != null) duplicateCaseButton.clicked -= DuplicateSelectedCase;
        if (deleteCaseButton != null) deleteCaseButton.clicked -= DeleteSelectedCase;
        indoorTemperatureField?.UnregisterValueChangedCallback(OnIndoorTemperatureChanged);
        indoorHumidityField?.UnregisterValueChangedCallback(OnIndoorHumidityChanged);
        outdoorTemperatureField?.UnregisterValueChangedCallback(OnOutdoorTemperatureChanged);
        outdoorHumidityField?.UnregisterValueChangedCallback(OnOutdoorHumidityChanged);
        setTemperatureField?.UnregisterValueChangedCallback(OnSetTemperatureChanged);
        targetSimulationTimeField?.UnregisterValueChangedCallback(OnTargetSimulationTimeChanged);
        caseList?.contentContainer.Clear();
        definitions.Clear();
        userCases.Clear();
        initialConditions.Clear();
        caseStore = null;
        caseList = null;
        caseCountValue = null;
        headerCaseValue = null;
        selectedSourceValue = null;
        selectedNameValue = null;
        selectedDescriptionValue = null;
        selectedDxValue = null;
        selectedFluidTauValue = null;
        selectedThermalTauValue = null;
        selectedTurbulenceValue = null;
        selectedStorageValue = null;
        selectedCreatedValue = null;
        selectionStatus = null;
        indoorTemperatureField = null;
        indoorHumidityField = null;
        outdoorTemperatureField = null;
        outdoorHumidityField = null;
        setTemperatureField = null;
        targetSimulationTimeField = null;
        newCaseButton = null;
        duplicateCaseButton = null;
        deleteCaseButton = null;
        selectedDefinition = default;
        SelectedCaseChanged = null;
    }

    private void RebuildDefinitionsAndList()
    {
        definitions.Clear();
        IReadOnlyList<SimulationSetupCaseDefinition> builtIn = SimulationSetupCaseCatalog.All;
        for (int i = 0; i < builtIn.Count; i++) definitions.Add(builtIn[i]);
        for (int i = 0; i < userCases.Count; i++) definitions.Add(new SimulationSetupCaseDefinition(userCases[i]));
        for (int i = 0; i < definitions.Count; i++)
            GetInitialConditions(definitions[i].Id);
        BuildCaseList();
        caseCountValue.text = definitions.Count.ToString(CultureInfo.InvariantCulture) + " cases";
    }

    private void BuildCaseList()
    {
        ClearCaseButtons();
        caseList.contentContainer.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            SimulationSetupCaseDefinition definition = definitions[i];
            string elementId = definition.Id.Replace(':', '_');
            var button = new Button { name = "CaseListItem_" + elementId, tooltip = definition.Description };
            button.AddToClassList("case-list-item");

            var nameLabel = new Label(definition.Name) { name = "CaseListName_" + elementId };
            nameLabel.AddToClassList("case-list-item__name");
            button.Add(nameLabel);

            var summaryLabel = new Label(BuildSummary(definition)) { name = "CaseListSummary_" + elementId };
            summaryLabel.AddToClassList("case-list-item__summary");
            button.Add(summaryLabel);

            string capturedId = definition.Id;
            Action handler = () => SelectCase(capturedId);
            button.clicked += handler;
            caseButtons.Add(definition.Id, button);
            clickHandlers.Add(definition.Id, handler);
            caseList.Add(button);
        }
    }

    private bool SelectCase(string caseId, bool notify)
    {
        int index = FindDefinitionIndex(caseId);
        if (index < 0 || !caseButtons.ContainsKey(caseId)) return false;

        selectedDefinition = definitions[index];
        foreach (KeyValuePair<string, Button> entry in caseButtons)
            entry.Value.EnableInClassList(SelectedClassName, entry.Key == caseId);

        headerCaseValue.text = "CASE  " + selectedDefinition.Name;
        selectedSourceValue.text = selectedDefinition.IsBuiltIn ? selectedDefinition.Group + " / Built-in" : "User / JSON";
        selectedNameValue.text = selectedDefinition.Name;
        selectedDescriptionValue.text = selectedDefinition.Description;
        selectedDxValue.text = selectedDefinition.DxPhys.ToString("F3", CultureInfo.InvariantCulture) + " m";
        selectedFluidTauValue.text = selectedDefinition.TauFluidMin.ToString("F3", CultureInfo.InvariantCulture);
        selectedThermalTauValue.text = selectedDefinition.TauThermalMin.ToString("F3", CultureInfo.InvariantCulture);
        selectedTurbulenceValue.text = selectedDefinition.Turbulence;
        selectedStorageValue.text = selectedDefinition.IsBuiltIn ? "Built-in / read-only" : "JSON user case";
        selectedCreatedValue.text = selectedDefinition.IsBuiltIn ? "Project preset" : FormatCreatedTimestamp(selectedDefinition.CreatedUtc);
        PushInitialConditionsToUi();
        deleteCaseButton.SetEnabled(!selectedDefinition.IsBuiltIn);
        deleteCaseButton.tooltip = selectedDefinition.IsBuiltIn
            ? "Built-in cases cannot be deleted."
            : "Delete this user case from JSON storage.";

        if (notify) SelectedCaseChanged?.Invoke(selectedDefinition);
        return true;
    }

    private EditableSimulationSetupInitialConditions GetSelectedInitialConditions()
    {
        return GetInitialConditions(selectedDefinition.Id);
    }

    private EditableSimulationSetupInitialConditions GetInitialConditions(string caseId)
    {
        if (string.IsNullOrEmpty(caseId))
            return new EditableSimulationSetupInitialConditions();
        if (!initialConditions.TryGetValue(caseId, out EditableSimulationSetupInitialConditions value))
        {
            value = new EditableSimulationSetupInitialConditions();
            initialConditions[caseId] = value;
        }
        return value;
    }

    private void PushInitialConditionsToUi()
    {
        EditableSimulationSetupInitialConditions value = GetSelectedInitialConditions();
        indoorTemperatureField.SetValueWithoutNotify(value.indoorTemperatureDegC);
        indoorHumidityField.SetValueWithoutNotify(value.indoorHumidityPercent);
        outdoorTemperatureField.SetValueWithoutNotify(value.outdoorTemperatureDegC);
        outdoorHumidityField.SetValueWithoutNotify(value.outdoorHumidityPercent);
        setTemperatureField.SetValueWithoutNotify(value.setTemperatureDegC);
        targetSimulationTimeField.SetValueWithoutNotify(value.targetSimulationTimeSeconds);
        ValidateAndShowInitialConditions();
    }

    private void OnIndoorTemperatureChanged(ChangeEvent<float> evt)
    {
        GetSelectedInitialConditions().indoorTemperatureDegC = evt.newValue;
        ValidateAndShowInitialConditions();
    }

    private void OnIndoorHumidityChanged(ChangeEvent<float> evt)
    {
        EditableSimulationSetupInitialConditions value = GetSelectedInitialConditions();
        value.indoorHumidityPercent = Mathf.Clamp(evt.newValue, 0f, 100f);
        indoorHumidityField.SetValueWithoutNotify(value.indoorHumidityPercent);
        ValidateAndShowInitialConditions();
    }

    private void OnOutdoorTemperatureChanged(ChangeEvent<float> evt)
    {
        GetSelectedInitialConditions().outdoorTemperatureDegC = evt.newValue;
        ValidateAndShowInitialConditions();
    }

    private void OnOutdoorHumidityChanged(ChangeEvent<float> evt)
    {
        EditableSimulationSetupInitialConditions value = GetSelectedInitialConditions();
        value.outdoorHumidityPercent = Mathf.Clamp(evt.newValue, 0f, 100f);
        outdoorHumidityField.SetValueWithoutNotify(value.outdoorHumidityPercent);
        ValidateAndShowInitialConditions();
    }

    private void OnSetTemperatureChanged(ChangeEvent<float> evt)
    {
        GetSelectedInitialConditions().setTemperatureDegC = evt.newValue;
        ValidateAndShowInitialConditions();
    }

    private void OnTargetSimulationTimeChanged(ChangeEvent<float> evt)
    {
        EditableSimulationSetupInitialConditions value = GetSelectedInitialConditions();
        value.targetSimulationTimeSeconds = Mathf.Max(0f, evt.newValue);
        targetSimulationTimeField.SetValueWithoutNotify(value.targetSimulationTimeSeconds);
        ValidateAndShowInitialConditions();
    }

    private void ValidateAndShowInitialConditions()
    {
        if (TryValidateInitialConditions(GetSelectedInitialConditions(), out string issue))
            ShowStatus("Initial conditions are valid and staged until Run.", false);
        else
            ShowStatus(issue, true);
    }

    private static bool TryValidateInitialConditions(
        EditableSimulationSetupInitialConditions value, out string issue)
    {
        if (!IsFinite(value.indoorTemperatureDegC) || value.indoorTemperatureDegC < -30f ||
            value.indoorTemperatureDegC > 60f)
        {
            issue = "Indoor temperature must be between -30 and 60 °C.";
            return false;
        }
        if (!IsFinite(value.outdoorTemperatureDegC) || value.outdoorTemperatureDegC < -50f ||
            value.outdoorTemperatureDegC > 70f)
        {
            issue = "Outdoor temperature must be between -50 and 70 °C.";
            return false;
        }
        if (!IsFinite(value.indoorHumidityPercent) || value.indoorHumidityPercent < 0f ||
            value.indoorHumidityPercent > 100f || !IsFinite(value.outdoorHumidityPercent) ||
            value.outdoorHumidityPercent < 0f || value.outdoorHumidityPercent > 100f)
        {
            issue = "Indoor and outdoor humidity must be between 0 and 100%.";
            return false;
        }
        if (!IsFinite(value.setTemperatureDegC) || value.setTemperatureDegC < -30f ||
            value.setTemperatureDegC > 60f)
        {
            issue = "Set temperature must be between -30 and 60 °C.";
            return false;
        }
        if (!IsFinite(value.targetSimulationTimeSeconds) || value.targetSimulationTimeSeconds <= 0f)
        {
            issue = "Target simulation time must be greater than 0 seconds.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private bool TrySaveUserCases(out string issue)
    {
        if (caseStore == null)
        {
            issue = "Case store is unavailable.";
            return false;
        }

        return caseStore.Save(userCases, out issue);
    }

    private void ShowStatus(string message, bool isError)
    {
        selectionStatus.text = message;
        selectionStatus.tooltip = caseStore != null ? caseStore.DisplayLocation : string.Empty;
        selectionStatus.EnableInClassList("case-selection__status--error", isError);
    }

    private void ClearCaseButtons()
    {
        foreach (KeyValuePair<string, Action> entry in clickHandlers)
            if (caseButtons.TryGetValue(entry.Key, out Button button)) button.clicked -= entry.Value;
        clickHandlers.Clear();
        caseButtons.Clear();
    }

    private int FindDefinitionIndex(string id)
    {
        for (int i = 0; i < definitions.Count; i++)
            if (string.Equals(definitions[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    private int FindUserCaseIndex(string id)
    {
        for (int i = 0; i < userCases.Count; i++)
            if (string.Equals(userCases[i].id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    private string GenerateUniqueName(string baseName)
    {
        string candidate = baseName;
        int suffix = 1;
        while (ContainsCaseName(candidate))
        {
            candidate = baseName + "_" + suffix.ToString("D2", CultureInfo.InvariantCulture);
            suffix++;
        }
        return candidate;
    }

    private bool ContainsCaseName(string name)
    {
        for (int i = 0; i < definitions.Count; i++)
            if (string.Equals(definitions[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static SimulationSetupUserCase CreateUserCase(SimulationSetupCaseDefinition source,
        string name, string description, DateTime now)
    {
        string timestamp = now.ToString("O", CultureInfo.InvariantCulture);
        return new SimulationSetupUserCase
        {
            id = "user:" + Guid.NewGuid().ToString("N"),
            name = name,
            description = description,
            basedOnPreset = source.BasedOnPreset.ToString(),
            dxPhys = source.DxPhys,
            tauFluidMin = source.TauFluidMin,
            tauThermalMin = source.TauThermalMin,
            turbulence = source.Turbulence,
            createdUtc = timestamp,
            modifiedUtc = timestamp
        };
    }

    private static string BuildSummary(SimulationSetupCaseDefinition definition)
    {
        return string.Format(CultureInfo.InvariantCulture,
            "{0}  |  tauF {1:F3}  tauT {2:F3}  |  {3}", definition.Group,
            definition.TauFluidMin, definition.TauThermalMin, definition.Turbulence);
    }

    private static string FormatCreatedTimestamp(string timestamp)
    {
        return DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "Unknown";
    }
}
