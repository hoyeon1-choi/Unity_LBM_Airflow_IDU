param(
    [double]$DurationSeconds = 10.0,
    [double]$StepSizeSeconds = 0.1,
    [double]$ControlJumpAtSeconds = 3.0,
    [int]$TimeoutMs = 30000,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$hostExe = Join-Path $projectRoot "FmuHost\bin\Debug\net8.0\FmuHost.exe"
$plugin = Join-Path $projectRoot "Assets\Plugins\x86_64\FmuNativePlugin.dll"
$fmuCacheRoot = Join-Path ([IO.Path]::GetDirectoryName($env:LOCALAPPDATA)) "LocalLow\DefaultCompany\MyLBM\FMUCache"
$cache = Get-ChildItem $fmuCacheRoot -Directory -Filter "MULTIV_FMU_WARPPER_*" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $cache) { throw "Product FMU cache was not found." }
if (-not (Test-Path $hostExe)) { throw "FmuHost was not built: $hostExe" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot "Temp\CoSimulationTests\product_substep_timings.csv"
}
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null

function Send-Command([string]$pipeName, [string]$request, [int]$timeout) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(".", $pipeName, [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect($timeout)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 4096, $true)
        try {
            $writer.AutoFlush = $true
            $writer.WriteLine($request)
            $task = $reader.ReadLineAsync()
            if (-not $task.Wait($timeout)) { throw "Command timed out: $request" }
            $response = $task.GetAwaiter().GetResult()
            if (-not $response.StartsWith("ok=1")) { throw "Command failed: $response" }
            return $response
        }
        finally { $writer.Dispose(); $reader.Dispose() }
    }
    finally { $pipe.Dispose() }
}

$pipeName = "product_substep_" + [Guid]::NewGuid().ToString("N")
$hostLog = [IO.Path]::ChangeExtension($OutputPath, ".host.log")
$process = Start-Process -FilePath $hostExe -ArgumentList @(
    "--pipe", $pipeName, "--plugin", $plugin, "--log", $hostLog
) -PassThru -WindowStyle Hidden -WorkingDirectory $cache.FullName

$instance = "MULTIV_FMU_WARPPER"
$rows = [Collections.Generic.List[object]]::new()
try {
    foreach ($attempt in 1..30) {
        try { Send-Command $pipeName "ping" 500 | Out-Null; break }
        catch { if ($attempt -eq 30) { throw }; Start-Sleep -Milliseconds 100 }
    }

    $unzip = [Uri]::EscapeDataString($cache.FullName)
    Send-Command $pipeName "load instance=$instance unzip=$unzip logging=1" 10000 | Out-Null
    Send-Command $pipeName "setup instance=$instance start=0 stop=0 hasStop=0 tolerance=0.0001 toleranceDefined=1" 10000 | Out-Null
    Send-Command $pipeName "enter instance=$instance" 10000 | Out-Null

    $inputs = [ordered]@{
        Comp_CurFreq = 0; Fan_CurRPM = 0; reversing_valve_mode_flag = 0; MAIN_EEV_CurPulse = 10
    }
    foreach ($index in 1..5) {
        $prefix = "idu_{0:D2}" -f $index
        $inputs["${prefix}_fan_mode"] = 4
        $inputs["${prefix}_onoff"] = 1
        $inputs["${prefix}_pulse"] = 10
        $inputs["${prefix}_temp_air"] = 30
        $inputs["${prefix}_RH_air"] = 50
    }
    foreach ($entry in $inputs.GetEnumerator()) {
        Send-Command $pipeName "set instance=$instance name=$($entry.Key) value=$($entry.Value)" 5000 | Out-Null
    }
    Send-Command $pipeName "exit instance=$instance" $TimeoutMs | Out-Null

    $count = [Math]::Ceiling($DurationSeconds / $StepSizeSeconds)
    $controlJumpApplied = $false
    for ($index = 0; $index -lt $count; $index++) {
        $current = $index * $StepSizeSeconds
        $step = [Math]::Min($StepSizeSeconds, $DurationSeconds - $current)
        if (-not $controlJumpApplied -and $current -ge $ControlJumpAtSeconds) {
            foreach ($iduIndex in 1..5) {
                $prefix = "idu_{0:D2}" -f $iduIndex
                Send-Command $pipeName "set instance=$instance name=${prefix}_pulse value=100" 5000 | Out-Null
                Send-Command $pipeName "set instance=$instance name=${prefix}_RH_air value=100" 5000 | Out-Null
                if ($iduIndex -gt 1) {
                    Send-Command $pipeName "set instance=$instance name=${prefix}_temp_air value=20" 5000 | Out-Null
                }
            }
            Send-Command $pipeName "set instance=$instance name=MAIN_EEV_CurPulse value=100" 5000 | Out-Null
            $controlJumpApplied = $true
        }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $result = "OK"
        $errorText = ""
        try {
            $currentText = $current.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
            $stepText = $step.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
            Send-Command $pipeName "step instance=$instance current=$currentText step=$stepText" $TimeoutMs | Out-Null
        }
        catch { $result = "FAILED"; $errorText = $_.Exception.Message }
        $watch.Stop()
        $rows.Add([pscustomobject]@{
            Index=$index; CurrentTime=$current; StepSize=$step; Result=$result
            ElapsedMilliseconds=[Math]::Round($watch.Elapsed.TotalMilliseconds, 3); Error=$errorText
        })
        if ($result -ne "OK") { break }
    }
}
finally {
    $rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
    try { Send-Command $pipeName "shutdown" 2000 | Out-Null } catch {}
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(3000) | Out-Null }
    $process.Dispose()
}

$rows | Format-Table Index,CurrentTime,StepSize,Result,ElapsedMilliseconds -AutoSize
Write-Output "RESULT_CSV=$OutputPath"
if ($rows.Result -contains "FAILED") { exit 2 }
