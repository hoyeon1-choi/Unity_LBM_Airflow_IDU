using System.Text;
using UnityEngine;

public static class SimulationDiagnostics
{
    public static string BuildSimulationSummary(
        Vector3 domainSizeMeters,
        uint nx, uint ny, uint nz,
        float dxPhys,
        float dtPhys,
        ulong stepCount,
        float simulatedTimeSeconds,
        float tauF,
        float tauT,
        float nuPhys,
        float alphaPhys,
        float machNumber,
        float reynoldsNumberPhys,
        float prandtlNumber,
        float maxWindSpeedPhys,
        string collisionModelName,
        string turbulenceModelName,
        float turbulenceModelConstant,
        float turbulentPrandtl,
        bool wallFunctionEnabled,
        LBMZouHeBox[] zhBoxes)
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine("=== LBM Simulation Summary ===");
        sb.AppendLine($"Domain Size [m]         : {domainSizeMeters.x:F4} x {domainSizeMeters.y:F4} x {domainSizeMeters.z:F4}");
        sb.AppendLine($"Grid Size [cells]       : {nx} x {ny} x {nz}");
        sb.AppendLine($"dx_phys [m]             : {dxPhys:F6}");
        sb.AppendLine($"dt_phys [s]             : {dtPhys:E6}");
        sb.AppendLine($"Step Count              : {stepCount:N0}");
        sb.AppendLine($"Physical Time [s]       : {simulatedTimeSeconds:F6}");
        sb.AppendLine($"tau_f / tau_T           : {tauF:F6} / {tauT:F6}");
        sb.AppendLine($"nu_phys [m^2/s]         : {nuPhys:E6}");
        sb.AppendLine($"alpha_phys [m^2/s]      : {alphaPhys:E6}");
        sb.AppendLine($"Mach Number             : {machNumber:F6}");
        sb.AppendLine($"Reynolds Number (phys)  : {reynoldsNumberPhys:F3}");
        sb.AppendLine($"Prandtl Number          : {prandtlNumber:F6}");
        sb.AppendLine($"Max Wind Speed [m/s]    : {maxWindSpeedPhys:F6}");
        sb.AppendLine($"Collision Model          : {collisionModelName}");
        sb.AppendLine($"Turbulence Model         : {turbulenceModelName}");
        sb.AppendLine($"SGS Constant             : {turbulenceModelConstant:F6}");
        sb.AppendLine($"Turbulent Prandtl        : {turbulentPrandtl:F6}");
        sb.AppendLine($"Wall Function            : {(wallFunctionEnabled ? "On" : "Off")}");
        sb.AppendLine();

        if (zhBoxes == null || zhBoxes.Length == 0)
        {
            sb.AppendLine("No active Zou-He patches.");
            return sb.ToString();
        }

        sb.AppendLine("=== Boundary Patches ===");
        foreach (var box in zhBoxes)
        {
            if (box == null || !box.Power)
                continue;

            sb.AppendLine(box.GetSummaryText(dxPhys));
            sb.AppendLine();
        }

        return sb.ToString();
    }
}