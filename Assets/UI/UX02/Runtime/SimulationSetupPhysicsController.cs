using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

public readonly struct SimulationSetupPhysicsConfiguration
{
    public string CollisionModel { get; }
    public string TurbulenceModel { get; }
    public float TurbulenceConstant { get; }
    public float TurbulentPrandtl { get; }
    public bool ThermalEnabled { get; }
    public float TemperatureMinDegC { get; }
    public float TemperatureMaxDegC { get; }
    public float ReferenceTemperatureDegC { get; }
    public float PrandtlTarget { get; }
    public float TauThermalMin { get; }
    public bool BuoyancyEnabled { get; }
    public string BuoyancyModel { get; }
    public float ThermalExpansionBeta { get; }
    public float GravityPhysicalY { get; }
    public float GravityLatticeY { get; }

    public SimulationSetupPhysicsConfiguration(
        string collisionModel, string turbulenceModel, float turbulenceConstant,
        float turbulentPrandtl, bool thermalEnabled, float temperatureMinDegC,
        float temperatureMaxDegC, float referenceTemperatureDegC, float prandtlTarget,
        float tauThermalMin, bool buoyancyEnabled, string buoyancyModel,
        float thermalExpansionBeta, float gravityPhysicalY, float gravityLatticeY)
    {
        CollisionModel = collisionModel;
        TurbulenceModel = turbulenceModel;
        TurbulenceConstant = turbulenceConstant;
        TurbulentPrandtl = turbulentPrandtl;
        ThermalEnabled = thermalEnabled;
        TemperatureMinDegC = temperatureMinDegC;
        TemperatureMaxDegC = temperatureMaxDegC;
        ReferenceTemperatureDegC = referenceTemperatureDegC;
        PrandtlTarget = prandtlTarget;
        TauThermalMin = tauThermalMin;
        BuoyancyEnabled = buoyancyEnabled;
        BuoyancyModel = buoyancyModel;
        ThermalExpansionBeta = thermalExpansionBeta;
        GravityPhysicalY = gravityPhysicalY;
        GravityLatticeY = gravityLatticeY;
    }
}

public sealed class SimulationSetupPhysicsController : IDisposable
{
    private sealed class EditableConfiguration
    {
        public string collisionModel = "MRT";
        public string turbulenceModel = "Off";
        public float turbulenceConstant = 0.03f;
        public float turbulentPrandtl = 0.7f;
        public bool thermalEnabled = true;
        public float temperatureMinDegC;
        public float temperatureMaxDegC = 30f;
        public float referenceTemperatureDegC = 30f;
        public float prandtlTarget = 0.71f;
        public float tauThermalMin = 0.56f;
        public bool buoyancyEnabled = true;
        public string buoyancyModel = "Boussinesq";
        public float thermalExpansionBeta = 0.05f;
        public float gravityPhysicalY = -9.81f;
        public float gravityLatticeY;

        public SimulationSetupPhysicsConfiguration Snapshot()
        {
            return new SimulationSetupPhysicsConfiguration(
                collisionModel, turbulenceModel, turbulenceConstant, turbulentPrandtl,
                thermalEnabled, temperatureMinDegC, temperatureMaxDegC,
                referenceTemperatureDegC, prandtlTarget, tauThermalMin,
                buoyancyEnabled, buoyancyModel, thermalExpansionBeta,
                gravityPhysicalY, gravityLatticeY);
        }
    }

    private static readonly List<string> CollisionChoices = new List<string> { "MRT" };
    private static readonly List<string> TurbulenceChoices =
        new List<string> { "Off", "Smagorinsky", "WALE" };
    private static readonly List<string> BuoyancyChoices =
        new List<string> { "Boussinesq" };

    private readonly Dictionary<string, EditableConfiguration> configurations =
        new Dictionary<string, EditableConfiguration>(StringComparer.Ordinal);

    private SimulationSetupCaseController caseSelection;
    private SimulationSetupModelController modelSelection;
    private ISimulationSetupSolverStateSource solverStateSource;
    private DropdownField collisionModelField;
    private Label fluidLatticeValue;
    private Label thermalLatticeValue;
    private DropdownField turbulenceModelField;
    private FloatField turbulenceConstantField;
    private FloatField turbulentPrandtlField;
    private Toggle thermalEnabledToggle;
    private FloatField temperatureMinField;
    private FloatField temperatureMaxField;
    private FloatField referenceTemperatureField;
    private FloatField prandtlTargetField;
    private Label tauThermalMinValue;
    private Toggle buoyancyEnabledToggle;
    private DropdownField buoyancyModelField;
    private FloatField thermalExpansionBetaField;
    private Label gravityPhysicalValue;
    private Label gravityLatticeValue;
    private Label physicsStatus;
    private EditableConfiguration current;
    private string currentCaseId = string.Empty;
    private string currentCaseName = string.Empty;
    private bool dirty = true;
    private bool updatingUi;
    private bool isValid;

    public bool HasConfiguration => current != null;
    public bool IsValid => current != null && isValid;
    public SimulationSetupPhysicsConfiguration CurrentConfiguration =>
        current != null ? current.Snapshot() : default;

    public event Action<SimulationSetupPhysicsConfiguration> ConfigurationChanged;

    public bool Initialize(
        VisualElement documentRoot,
        SimulationSetupCaseController cases,
        SimulationSetupModelController models,
        out string issue,
        ISimulationSetupSolverStateSource solverState = null)
    {
        Dispose();
        if (documentRoot == null || cases == null || models == null)
        {
            issue = "Physics UI root, Case controller, or Model controller is missing.";
            return false;
        }

        collisionModelField = documentRoot.Q<DropdownField>("PhysicsCollisionModelField");
        fluidLatticeValue = documentRoot.Q<Label>("PhysicsFluidLatticeValue");
        thermalLatticeValue = documentRoot.Q<Label>("PhysicsThermalLatticeValue");
        turbulenceModelField = documentRoot.Q<DropdownField>("PhysicsTurbulenceModelField");
        turbulenceConstantField = documentRoot.Q<FloatField>("PhysicsTurbulenceConstantField");
        turbulentPrandtlField = documentRoot.Q<FloatField>("PhysicsTurbulentPrandtlField");
        thermalEnabledToggle = documentRoot.Q<Toggle>("PhysicsThermalEnabledToggle");
        temperatureMinField = documentRoot.Q<FloatField>("PhysicsTemperatureMinField");
        temperatureMaxField = documentRoot.Q<FloatField>("PhysicsTemperatureMaxField");
        referenceTemperatureField = documentRoot.Q<FloatField>("PhysicsReferenceTemperatureField");
        prandtlTargetField = documentRoot.Q<FloatField>("PhysicsPrandtlTargetField");
        tauThermalMinValue = documentRoot.Q<Label>("PhysicsTauThermalMinValue");
        buoyancyEnabledToggle = documentRoot.Q<Toggle>("PhysicsBuoyancyEnabledToggle");
        buoyancyModelField = documentRoot.Q<DropdownField>("PhysicsBuoyancyModelField");
        thermalExpansionBetaField = documentRoot.Q<FloatField>("PhysicsThermalExpansionBetaField");
        gravityPhysicalValue = documentRoot.Q<Label>("PhysicsGravityPhysicalValue");
        gravityLatticeValue = documentRoot.Q<Label>("PhysicsGravityLatticeValue");
        physicsStatus = documentRoot.Q<Label>("PhysicsConfigurationStatus");

        if (collisionModelField == null || fluidLatticeValue == null || thermalLatticeValue == null ||
            turbulenceModelField == null || turbulenceConstantField == null ||
            turbulentPrandtlField == null || thermalEnabledToggle == null ||
            temperatureMinField == null || temperatureMaxField == null ||
            referenceTemperatureField == null || prandtlTargetField == null ||
            tauThermalMinValue == null || buoyancyEnabledToggle == null ||
            buoyancyModelField == null || thermalExpansionBetaField == null ||
            gravityPhysicalValue == null || gravityLatticeValue == null || physicsStatus == null)
        {
            issue = "One or more Physics configuration UI elements are missing.";
            Dispose();
            return false;
        }

        caseSelection = cases;
        modelSelection = models;
        solverStateSource = solverState;
        collisionModelField.choices = CollisionChoices;
        collisionModelField.SetValueWithoutNotify("MRT");
        collisionModelField.SetEnabled(false);
        collisionModelField.tooltip = "Only MRT collision is implemented by the current compute shader.";
        turbulenceModelField.choices = TurbulenceChoices;
        turbulenceModelField.tooltip = "Supported shader modes: Off, Smagorinsky, and WALE.";
        buoyancyModelField.choices = BuoyancyChoices;
        buoyancyModelField.SetValueWithoutNotify("Boussinesq");
        buoyancyModelField.SetEnabled(false);
        buoyancyModelField.tooltip = "Only Boussinesq buoyancy is implemented by the current solver.";
        thermalEnabledToggle.SetValueWithoutNotify(true);
        thermalEnabledToggle.SetEnabled(false);
        thermalEnabledToggle.tooltip = "The current ThermalSolver always advances the D3Q7 thermal field.";
        fluidLatticeValue.text = "D3Q19 MRT";
        thermalLatticeValue.text = "D3Q7 MRT";

        RegisterCallbacks();
        caseSelection.SelectedCaseChanged += OnCaseChanged;
        modelSelection.SelectedModelChanged += OnModelChanged;
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
        if (caseSelection == null || modelSelection == null)
            return;

        modelSelection.RefreshIfNeeded();
        string caseId = caseSelection.SelectedCaseId;
        if (!configurations.TryGetValue(caseId, out current))
        {
            current = CreateConfiguration(caseSelection.SelectedDefinition, ReadSolverState());
            configurations[caseId] = current;
        }

        currentCaseId = caseId;
        currentCaseName = caseSelection.SelectedDefinition.Name;
        SimulationSetupSolverStateSnapshot solverState = ReadSolverState();
        if (solverState.IsAvailable)
        {
            current.gravityPhysicalY = solverState.GravityPhysicalY;
            current.gravityLatticeY = solverState.GravityLatticeY;
        }

        dirty = false;
        PushConfigurationToUi();
        ValidateAndShowStatus(false);
    }

    public void Dispose()
    {
        UnregisterCallbacks();
        if (caseSelection != null)
            caseSelection.SelectedCaseChanged -= OnCaseChanged;
        if (modelSelection != null)
            modelSelection.SelectedModelChanged -= OnModelChanged;

        configurations.Clear();
        caseSelection = null;
        modelSelection = null;
        solverStateSource = null;
        collisionModelField = null;
        fluidLatticeValue = null;
        thermalLatticeValue = null;
        turbulenceModelField = null;
        turbulenceConstantField = null;
        turbulentPrandtlField = null;
        thermalEnabledToggle = null;
        temperatureMinField = null;
        temperatureMaxField = null;
        referenceTemperatureField = null;
        prandtlTargetField = null;
        tauThermalMinValue = null;
        buoyancyEnabledToggle = null;
        buoyancyModelField = null;
        thermalExpansionBetaField = null;
        gravityPhysicalValue = null;
        gravityLatticeValue = null;
        physicsStatus = null;
        current = null;
        currentCaseId = string.Empty;
        currentCaseName = string.Empty;
        dirty = true;
        updatingUi = false;
        isValid = false;
        ConfigurationChanged = null;
    }

    private EditableConfiguration CreateConfiguration(
        SimulationSetupCaseDefinition selectedCase,
        SimulationSetupSolverStateSnapshot solverState)
    {
        var configuration = new EditableConfiguration
        {
            tauThermalMin = selectedCase.TauThermalMin
        };
        ParseTurbulence(selectedCase.Turbulence, out configuration.turbulenceModel,
            out configuration.turbulenceConstant);
        if (solverState.IsAvailable)
        {
            configuration.temperatureMinDegC = solverState.TemperatureMinDegC;
            configuration.temperatureMaxDegC = solverState.TemperatureMaxDegC;
            configuration.referenceTemperatureDegC = solverState.ReferenceTemperatureDegC;
            configuration.prandtlTarget = solverState.PrandtlTarget;
            configuration.turbulentPrandtl = solverState.TurbulentPrandtl;
            configuration.thermalExpansionBeta = solverState.ThermalExpansionBeta;
            configuration.gravityPhysicalY = solverState.GravityPhysicalY;
            configuration.gravityLatticeY = solverState.GravityLatticeY;
            configuration.buoyancyEnabled = Mathf.Abs(solverState.ThermalExpansionBeta) > 0.0000001f &&
                                            Mathf.Abs(solverState.GravityPhysicalY) > 0.0000001f;
        }

        return configuration;
    }

    private SimulationSetupSolverStateSnapshot ReadSolverState()
    {
        return solverStateSource != null
            ? solverStateSource.Read()
            : SimulationSetupSolverStateSnapshot.Unavailable("Solver state adapter is not configured.");
    }

    private void OnCaseChanged(SimulationSetupCaseDefinition definition)
    {
        MarkDirty();
    }

    private void OnModelChanged(SimulationSetupModelDefinition definition)
    {
        MarkDirty();
    }

    private void PushConfigurationToUi()
    {
        updatingUi = true;
        collisionModelField.SetValueWithoutNotify(current.collisionModel);
        turbulenceModelField.SetValueWithoutNotify(current.turbulenceModel);
        turbulenceConstantField.SetValueWithoutNotify(current.turbulenceConstant);
        turbulentPrandtlField.SetValueWithoutNotify(current.turbulentPrandtl);
        thermalEnabledToggle.SetValueWithoutNotify(current.thermalEnabled);
        temperatureMinField.SetValueWithoutNotify(current.temperatureMinDegC);
        temperatureMaxField.SetValueWithoutNotify(current.temperatureMaxDegC);
        referenceTemperatureField.SetValueWithoutNotify(current.referenceTemperatureDegC);
        prandtlTargetField.SetValueWithoutNotify(current.prandtlTarget);
        tauThermalMinValue.text = current.tauThermalMin.ToString("0.000", CultureInfo.InvariantCulture);
        buoyancyEnabledToggle.SetValueWithoutNotify(current.buoyancyEnabled);
        buoyancyModelField.SetValueWithoutNotify(current.buoyancyModel);
        thermalExpansionBetaField.SetValueWithoutNotify(current.thermalExpansionBeta);
        gravityPhysicalValue.text = $"(0, {current.gravityPhysicalY:0.###}, 0) m/s²";
        gravityLatticeValue.text = Mathf.Abs(current.gravityLatticeY) > 0f
            ? $"(0, {current.gravityLatticeY:0.######}, 0) lu/step²"
            : "Pending scaling";
        UpdateConditionalControls();
        updatingUi = false;
    }

    private void UpdateConditionalControls()
    {
        bool turbulenceEnabled = current != null && current.turbulenceModel != "Off";
        turbulenceConstantField.SetEnabled(turbulenceEnabled);
        turbulentPrandtlField.SetEnabled(turbulenceEnabled);
        thermalExpansionBetaField.SetEnabled(current != null && current.buoyancyEnabled);
    }

    private void RegisterCallbacks()
    {
        turbulenceModelField.RegisterValueChangedCallback(OnTurbulenceModelChanged);
        turbulenceConstantField.RegisterValueChangedCallback(OnTurbulenceConstantChanged);
        turbulentPrandtlField.RegisterValueChangedCallback(OnTurbulentPrandtlChanged);
        temperatureMinField.RegisterValueChangedCallback(OnTemperatureMinChanged);
        temperatureMaxField.RegisterValueChangedCallback(OnTemperatureMaxChanged);
        referenceTemperatureField.RegisterValueChangedCallback(OnReferenceTemperatureChanged);
        prandtlTargetField.RegisterValueChangedCallback(OnPrandtlTargetChanged);
        buoyancyEnabledToggle.RegisterValueChangedCallback(OnBuoyancyEnabledChanged);
        thermalExpansionBetaField.RegisterValueChangedCallback(OnThermalExpansionBetaChanged);
    }

    private void UnregisterCallbacks()
    {
        turbulenceModelField?.UnregisterValueChangedCallback(OnTurbulenceModelChanged);
        turbulenceConstantField?.UnregisterValueChangedCallback(OnTurbulenceConstantChanged);
        turbulentPrandtlField?.UnregisterValueChangedCallback(OnTurbulentPrandtlChanged);
        temperatureMinField?.UnregisterValueChangedCallback(OnTemperatureMinChanged);
        temperatureMaxField?.UnregisterValueChangedCallback(OnTemperatureMaxChanged);
        referenceTemperatureField?.UnregisterValueChangedCallback(OnReferenceTemperatureChanged);
        prandtlTargetField?.UnregisterValueChangedCallback(OnPrandtlTargetChanged);
        buoyancyEnabledToggle?.UnregisterValueChangedCallback(OnBuoyancyEnabledChanged);
        thermalExpansionBetaField?.UnregisterValueChangedCallback(OnThermalExpansionBetaChanged);
    }

    private void OnTurbulenceModelChanged(ChangeEvent<string> evt)
    {
        if (!CanEdit()) return;
        current.turbulenceModel = TurbulenceChoices.Contains(evt.newValue) ? evt.newValue : "Off";
        UpdateConditionalControls();
        ValidateAndShowStatus(true);
    }

    private void OnTurbulenceConstantChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.turbulenceConstant = evt.newValue;
        ValidateAndShowStatus(true);
    }

    private void OnTurbulentPrandtlChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.turbulentPrandtl = evt.newValue;
        ValidateAndShowStatus(true);
    }

    private void OnTemperatureMinChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.temperatureMinDegC = evt.newValue;
        ValidateAndShowStatus(true);
    }

    private void OnTemperatureMaxChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.temperatureMaxDegC = evt.newValue;
        ValidateAndShowStatus(true);
    }

    private void OnReferenceTemperatureChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.referenceTemperatureDegC = evt.newValue;
        ValidateAndShowStatus(true);
    }

    private void OnPrandtlTargetChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.prandtlTarget = evt.newValue;
        ValidateAndShowStatus(true);
    }

    private void OnBuoyancyEnabledChanged(ChangeEvent<bool> evt)
    {
        if (!CanEdit()) return;
        current.buoyancyEnabled = evt.newValue;
        UpdateConditionalControls();
        ValidateAndShowStatus(true);
    }

    private void OnThermalExpansionBetaChanged(ChangeEvent<float> evt)
    {
        if (!CanEdit()) return;
        current.thermalExpansionBeta = evt.newValue;
        ValidateAndShowStatus(true);
    }

    private bool CanEdit()
    {
        return !updatingUi && current != null;
    }

    private void ValidateAndShowStatus(bool notify)
    {
        string issue = ValidateConfiguration(current);
        isValid = string.IsNullOrEmpty(issue);
        physicsStatus.text = isValid
            ? $"Physics settings are staged for Case '{currentCaseName}' and will be applied on Start."
            : issue;
        physicsStatus.EnableInClassList("physics-status--error", !isValid);
        if (notify)
            ConfigurationChanged?.Invoke(current.Snapshot());
    }

    private static string ValidateConfiguration(EditableConfiguration configuration)
    {
        if (configuration == null)
            return "Physics configuration is unavailable.";
        if (!IsFinite(configuration.turbulenceConstant) ||
            !IsFinite(configuration.turbulentPrandtl) ||
            !IsFinite(configuration.temperatureMinDegC) ||
            !IsFinite(configuration.temperatureMaxDegC) ||
            !IsFinite(configuration.referenceTemperatureDegC) ||
            !IsFinite(configuration.prandtlTarget) ||
            !IsFinite(configuration.thermalExpansionBeta))
            return "Physics values must be finite numbers.";
        if (configuration.turbulenceModel != "Off" && configuration.turbulenceConstant < 0f)
            return "The turbulence model constant must be zero or greater.";
        if (configuration.turbulenceModel != "Off" && configuration.turbulentPrandtl <= 0f)
            return "Turbulent Prandtl must be greater than zero.";
        if (configuration.temperatureMaxDegC <= configuration.temperatureMinDegC)
            return "Maximum temperature must be greater than minimum temperature.";
        if (configuration.referenceTemperatureDegC < configuration.temperatureMinDegC ||
            configuration.referenceTemperatureDegC > configuration.temperatureMaxDegC)
            return "Reference temperature must remain inside the configured temperature range.";
        if (configuration.prandtlTarget <= 0f)
            return "Target Prandtl number must be greater than zero.";
        if (configuration.buoyancyEnabled && configuration.thermalExpansionBeta < 0f)
            return "Thermal expansion beta must be zero or greater.";
        return string.Empty;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void ParseTurbulence(string value, out string model, out float constant)
    {
        model = "Off";
        constant = 0.03f;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("None", StringComparison.OrdinalIgnoreCase))
            return;

        if (value.StartsWith("WALE", StringComparison.OrdinalIgnoreCase))
            model = "WALE";
        else if (value.StartsWith("Smagorinsky", StringComparison.OrdinalIgnoreCase))
            model = "Smagorinsky";
        else
            return;

        int open = value.IndexOf('(');
        int close = value.IndexOf(')', open + 1);
        if (open >= 0 && close > open &&
            float.TryParse(value.Substring(open + 1, close - open - 1),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            constant = parsed;
        }
    }
}
