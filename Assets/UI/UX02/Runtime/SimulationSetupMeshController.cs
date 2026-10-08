using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class SimulationSetupMeshController : IDisposable
{
    public const float RequiredCellSizeMeters = 0.04f;

    private SimulationSetupCaseController caseSelection;
    private SimulationSetupModelController modelSelection;
    private Label modelNameValue;
    private FloatField cellSizeField;
    private Label cellSizeSourceValue;
    private Label domainSizeValue;
    private Label nxValue;
    private Label nyValue;
    private Label nzValue;
    private Label totalCellsValue;
    private Label distributionMemoryValue;
    private Label stateMemoryValue;
    private Label textureMemoryValue;
    private Label totalMemoryValue;
    private Label largestBufferMemoryValue;
    private Label bufferLimitValue;
    private Label memoryStatusValue;
    private Label estimateStatus;
    private bool dirty = true;
    private bool hasEstimate;
    private LbmGridMemoryEstimate currentEstimate;

    public bool HasEstimate => hasEstimate;
    public float CellSizeMeters => RequiredCellSizeMeters;
    public LbmGridMemoryEstimate CurrentEstimate => currentEstimate;

    public bool Initialize(
        VisualElement documentRoot,
        SimulationSetupCaseController cases,
        SimulationSetupModelController models,
        out string issue)
    {
        Dispose();
        if (documentRoot == null || cases == null || models == null)
        {
            issue = "Mesh UI root, Case controller, or Model controller is missing.";
            return false;
        }

        modelNameValue = documentRoot.Q<Label>("MeshModelNameValue");
        cellSizeField = documentRoot.Q<FloatField>("MeshCellSizeField");
        cellSizeSourceValue = documentRoot.Q<Label>("MeshCellSizeSourceValue");
        domainSizeValue = documentRoot.Q<Label>("MeshDomainSizeValue");
        nxValue = documentRoot.Q<Label>("MeshNxValue");
        nyValue = documentRoot.Q<Label>("MeshNyValue");
        nzValue = documentRoot.Q<Label>("MeshNzValue");
        totalCellsValue = documentRoot.Q<Label>("MeshTotalCellsValue");
        distributionMemoryValue = documentRoot.Q<Label>("MeshDistributionMemoryValue");
        stateMemoryValue = documentRoot.Q<Label>("MeshStateMemoryValue");
        textureMemoryValue = documentRoot.Q<Label>("MeshTextureMemoryValue");
        totalMemoryValue = documentRoot.Q<Label>("MeshTotalMemoryValue");
        largestBufferMemoryValue = documentRoot.Q<Label>("MeshLargestBufferMemoryValue");
        bufferLimitValue = documentRoot.Q<Label>("MeshBufferLimitValue");
        memoryStatusValue = documentRoot.Q<Label>("MeshMemoryStatusValue");
        estimateStatus = documentRoot.Q<Label>("MeshEstimateStatus");

        if (modelNameValue == null || cellSizeField == null || cellSizeSourceValue == null ||
            domainSizeValue == null || nxValue == null || nyValue == null || nzValue == null ||
            totalCellsValue == null || distributionMemoryValue == null || stateMemoryValue == null ||
            textureMemoryValue == null || totalMemoryValue == null ||
            largestBufferMemoryValue == null || bufferLimitValue == null ||
            memoryStatusValue == null || estimateStatus == null)
        {
            issue = "One or more Mesh configuration UI elements are missing.";
            Dispose();
            return false;
        }

        caseSelection = cases;
        modelSelection = models;
        caseSelection.SelectedCaseChanged += OnCaseChanged;
        modelSelection.SelectedModelChanged += OnModelChanged;
        cellSizeField.SetValueWithoutNotify(RequiredCellSizeMeters);
        cellSizeField.SetEnabled(false);
        cellSizeField.tooltip = "Case Study grid spacing is fixed at dxPhys = 0.040 m.";
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
        if (modelSelection == null || caseSelection == null)
            return;

        modelSelection.RefreshIfNeeded();
        dirty = false;
        cellSizeField.SetValueWithoutNotify(RequiredCellSizeMeters);
        SimulationSetupCaseDefinition selectedCase = caseSelection.SelectedDefinition;
        bool caseCellSizeMismatch = Mathf.Abs(selectedCase.DxPhys - RequiredCellSizeMeters) > 0.000001f;
        cellSizeSourceValue.text = caseCellSizeMismatch ? "Enforced 0.040 m" : "Case locked";

        if (!modelSelection.HasSelection)
        {
            ShowWaitingState();
            SetStatus("Select a loaded model before estimating the LBM grid.", false);
            return;
        }

        SimulationSetupModelDefinition model = modelSelection.SelectedModel;
        modelNameValue.text = model.Name;
        domainSizeValue.text = FormatVector(model.DomainSize) + " m";
        if (!LbmGridMemoryEstimator.TryEstimate(
                model.DomainSize, RequiredCellSizeMeters, out currentEstimate, out string issue))
        {
            hasEstimate = false;
            ClearEstimateValues();
            SetStatus(issue, true);
            return;
        }

        hasEstimate = true;
        nxValue.text = currentEstimate.Nx.ToString("N0");
        nyValue.text = currentEstimate.Ny.ToString("N0");
        nzValue.text = currentEstimate.Nz.ToString("N0");
        totalCellsValue.text = currentEstimate.CellCount.ToString("N0");
        distributionMemoryValue.text = FormatMiB(currentEstimate.DistributionBytes);
        stateMemoryValue.text = FormatMiB(currentEstimate.StateBufferBytes);
        textureMemoryValue.text = FormatMiB(currentEstimate.TextureBytes);
        totalMemoryValue.text = FormatMiB(currentEstimate.TotalBytes);
        largestBufferMemoryValue.text = FormatMiB(currentEstimate.LargestDistributionBufferBytes);
        bufferLimitValue.text = FormatMiB(LbmGridMemoryEstimator.MaxGraphicsBufferBytes);
        memoryStatusValue.text = currentEstimate.IsSingleBufferSafe ? "Within limit" : "Exceeds limit";
        memoryStatusValue.EnableInClassList(
            "mesh-memory__state--error", !currentEstimate.IsSingleBufferSafe);

        string constraintNote = caseCellSizeMismatch
            ? " The selected Case value differs, so the required 0.040 m spacing is enforced in this preview."
            : string.Empty;
        SetStatus(
            "Estimate matches ThermalSolver volume allocations; small boundary/debug buffers and driver overhead are excluded." +
            constraintNote,
            !currentEstimate.IsSingleBufferSafe || caseCellSizeMismatch);
    }

    public void Dispose()
    {
        if (caseSelection != null)
            caseSelection.SelectedCaseChanged -= OnCaseChanged;
        if (modelSelection != null)
            modelSelection.SelectedModelChanged -= OnModelChanged;

        caseSelection = null;
        modelSelection = null;
        modelNameValue = null;
        cellSizeField = null;
        cellSizeSourceValue = null;
        domainSizeValue = null;
        nxValue = null;
        nyValue = null;
        nzValue = null;
        totalCellsValue = null;
        distributionMemoryValue = null;
        stateMemoryValue = null;
        textureMemoryValue = null;
        totalMemoryValue = null;
        largestBufferMemoryValue = null;
        bufferLimitValue = null;
        memoryStatusValue = null;
        estimateStatus = null;
        currentEstimate = default;
        hasEstimate = false;
        dirty = true;
    }

    private void OnCaseChanged(SimulationSetupCaseDefinition definition)
    {
        MarkDirty();
    }

    private void OnModelChanged(SimulationSetupModelDefinition definition)
    {
        MarkDirty();
    }

    private void ShowWaitingState()
    {
        if (modelNameValue == null)
            return;

        modelNameValue.text = "No loaded model";
        cellSizeField.SetValueWithoutNotify(RequiredCellSizeMeters);
        cellSizeSourceValue.text = "Case locked";
        domainSizeValue.text = "—";
        ClearEstimateValues();
        hasEstimate = false;
    }

    private void ClearEstimateValues()
    {
        nxValue.text = "—";
        nyValue.text = "—";
        nzValue.text = "—";
        totalCellsValue.text = "—";
        distributionMemoryValue.text = "—";
        stateMemoryValue.text = "—";
        textureMemoryValue.text = "—";
        totalMemoryValue.text = "—";
        largestBufferMemoryValue.text = "—";
        bufferLimitValue.text = FormatMiB(LbmGridMemoryEstimator.MaxGraphicsBufferBytes);
        memoryStatusValue.text = "Waiting";
        memoryStatusValue.EnableInClassList("mesh-memory__state--error", false);
    }

    private void SetStatus(string message, bool isError)
    {
        estimateStatus.text = message ?? string.Empty;
        estimateStatus.EnableInClassList("mesh-estimate__status--error", isError);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"{value.x:0.###} x {value.y:0.###} x {value.z:0.###}";
    }

    private static string FormatMiB(long bytes)
    {
        return $"{LbmGridMemoryEstimator.BytesToMiB(bytes):N1} MiB";
    }
}
