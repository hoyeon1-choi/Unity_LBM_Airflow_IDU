param(
    [double[]]$StepSizes = @(0.1, 0.05, 0.02, 0.01, 0.005, 0.002),
    [double]$DurationSeconds = 3.2,
    [double]$Tolerance = 0.0001,
    [switch]$UsePersistentPipe,
    [switch]$UseBatchIo,
    [switch]$SkipUnchangedInputs,
    [switch]$RepeatInputsEachSecond,
    [switch]$NativeLogging,
    [int]$TimeoutMs = 30000,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$hostExe = Join-Path $projectRoot "FmuHost\bin\Debug\net8.0\FmuHost.exe"
$plugin = Join-Path $projectRoot "Assets\Plugins\x86_64\FmuNativePlugin.dll"
$fmuPath = Join-Path $projectRoot "Assets\StreamingAssets\FMU\product\MULTIV_FMU_WARPPER.fmu"
$cacheRoot = Join-Path $projectRoot "Temp\CoSimulationTests\ProductR1OnlySweepCache"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot "Temp\CoSimulationTests\product_r1_only_step_sweep.csv"
}
elseif (-not [IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $projectRoot $OutputPath
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$stepOutputPath = [IO.Path]::Combine(
    [IO.Path]::GetDirectoryName($OutputPath),
    [IO.Path]::GetFileNameWithoutExtension($OutputPath) + "_steps.csv")
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$script:persistentPipeName = ""
$script:persistentPipe = $null
$script:persistentWriter = $null
$script:persistentReader = $null
$script:lastInputValues = @{}

$hash = (Get-FileHash -LiteralPath $fmuPath -Algorithm SHA256).Hash.Substring(0, 16)
$cache = Join-Path $cacheRoot "MULTIV_FMU_WARPPER_$hash"
if (-not (Test-Path -LiteralPath (Join-Path $cache "modelDescription.xml"))) {
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($fmuPath, $cache)
}

function Send-Command([string]$pipeName, [string]$request, [int]$timeout) {
    if ($UsePersistentPipe) {
        if ($null -eq $script:persistentPipe -or -not $script:persistentPipe.IsConnected -or $script:persistentPipeName -ne $pipeName) {
            Close-PersistentPipe
            $script:persistentPipeName = $pipeName
            $script:persistentPipe = [IO.Pipes.NamedPipeClientStream]::new(".", $pipeName, [IO.Pipes.PipeDirection]::InOut)
            $script:persistentPipe.Connect($timeout)
            $script:persistentWriter = [IO.StreamWriter]::new($script:persistentPipe, [Text.UTF8Encoding]::new($false), 4096, $true)
            $script:persistentReader = [IO.StreamReader]::new($script:persistentPipe, [Text.Encoding]::UTF8, $false, 4096, $true)
            $script:persistentWriter.AutoFlush = $true
        }
        $script:persistentWriter.WriteLine($request + " keepAlive=1")
        $task = $script:persistentReader.ReadLineAsync()
        if (-not $task.Wait($timeout)) { throw "Command timed out: $request" }
        $response = $task.GetAwaiter().GetResult()
        if ($null -eq $response -or -not $response.StartsWith("ok=1")) {
            throw "Command failed: request=$request response=$response"
        }
        return $response
    }

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
            if ($null -eq $response -or -not $response.StartsWith("ok=1")) {
                throw "Command failed: request=$request response=$response"
            }
            return $response
        }
        finally {
            $writer.Dispose()
            $reader.Dispose()
        }
    }
    finally { $pipe.Dispose() }
}

function Close-PersistentPipe {
    if ($null -ne $script:persistentWriter) { $script:persistentWriter.Dispose() }
    if ($null -ne $script:persistentReader) { $script:persistentReader.Dispose() }
    if ($null -ne $script:persistentPipe) { $script:persistentPipe.Dispose() }
    $script:persistentWriter = $null
    $script:persistentReader = $null
    $script:persistentPipe = $null
    $script:persistentPipeName = ""
}

function Set-Real([string]$pipeName, [string]$name, [double]$value) {
    $text = $value.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
    Send-Command $pipeName "set instance=MULTIV_FMU_WARPPER name=$name value=$text" 5000 | Out-Null
}

function Set-Reals([string]$pipeName, [Collections.IDictionary]$inputValues) {
    $names = [Collections.Generic.List[string]]::new()
    $values = [Collections.Generic.List[string]]::new()
    foreach ($entry in $inputValues.GetEnumerator()) {
        $value = [double]$entry.Value
        if ($SkipUnchangedInputs -and $script:lastInputValues.ContainsKey($entry.Key) -and
            [Math]::Abs([double]$script:lastInputValues[$entry.Key] - $value) -le 1.0e-9) {
            continue
        }
        $names.Add([string]$entry.Key)
        $values.Add($value.ToString("R", [Globalization.CultureInfo]::InvariantCulture))
    }
    if ($names.Count -eq 0) { return }
    if ($UseBatchIo) {
        $encodedNames = [Uri]::EscapeDataString(($names -join "`n"))
        $encodedValues = [Uri]::EscapeDataString(($values -join "`n"))
        Send-Command $pipeName "setMany instance=MULTIV_FMU_WARPPER names=$encodedNames values=$encodedValues" 5000 | Out-Null
    }
    else {
        for ($i = 0; $i -lt $names.Count; $i++) {
            Set-Real $pipeName $names[$i] ([double]::Parse($values[$i], [Globalization.CultureInfo]::InvariantCulture))
        }
    }
    for ($i = 0; $i -lt $names.Count; $i++) { $script:lastInputValues[$names[$i]] = [double]$inputValues[$names[$i]] }
}

function New-ProductInputs([double]$mainEev, [double[]]$temperatures) {
    $inputs = [ordered]@{
        Comp_CurFreq = 0.0
        Fan_CurRPM = 0.0
        reversing_valve_mode_flag = 0.0
        MAIN_EEV_CurPulse = $mainEev
    }
    foreach ($index in 1..5) {
        $prefix = "idu_{0:D2}" -f $index
        $isR1 = $index -eq 1
        $inputs["${prefix}_onoff"] = $(if ($isR1) { 1.0 } else { 0.0 })
        $inputs["${prefix}_fan_mode"] = $(if ($isR1) { 4.0 } else { 0.0 })
        $inputs["${prefix}_pulse"] = 0.0
        $inputs["${prefix}_temp_air"] = $temperatures[$index - 1]
        $inputs["${prefix}_RH_air"] = 50.0
    }
    return $inputs
}

function Get-Real([string]$pipeName, [string]$name) {
    $response = Send-Command $pipeName "get instance=MULTIV_FMU_WARPPER name=$name" 5000
    if ($response -notmatch '(?:^|\s)value=([^\s]+)') { throw "Value missing in response: $response" }
    return [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
}

function Get-ReplayMainEev([double]$time) {
    if ($time -lt 1.0) { return 1970.0 }
    if ($time -lt 2.0) { return 1790.0 }
    if ($time -lt 3.0) { return 1670.0 }
    return 1550.0
}

$summaryRows = [Collections.Generic.List[object]]::new()
$stepRows = [Collections.Generic.List[object]]::new()
$roomTemperatures = @(28.67, 29.94, 29.94, 29.84, 29.84)

foreach ($stepSize in $StepSizes) {
    if ($stepSize -le 0.0) { throw "Step sizes must be positive." }
    $stepLabel = $stepSize.ToString("0.000000", [Globalization.CultureInfo]::InvariantCulture).Replace('.', '_')
    $pipeName = "product_r1_only_" + [Guid]::NewGuid().ToString("N")
    $hostLog = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) "product_r1_only_h_${stepLabel}.host.log"
    $nativeLog = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) "product_r1_only_h_${stepLabel}.native.log"
    $process = Start-Process -FilePath $hostExe -ArgumentList @(
        "--pipe", $pipeName, "--plugin", $plugin, "--log", $hostLog
    ) -PassThru -WindowStyle Hidden -WorkingDirectory $cache

    $result = "OK"
    $errorText = ""
    $completedSteps = 0
    $failureTime = $null
    $maxStepMs = 0.0
    $maxStepTime = $null
    $finalR1MassFlow = $null
    $finalR2MassFlow = $null
    $runWatch = [Diagnostics.Stopwatch]::StartNew()
    $script:lastInputValues = @{}

    try {
        foreach ($attempt in 1..30) {
            try { Send-Command $pipeName "ping" 500 | Out-Null; break }
            catch { if ($attempt -eq 30) { throw }; Start-Sleep -Milliseconds 100 }
        }

        $unzip = [Uri]::EscapeDataString($cache)
        $logValue = [Uri]::EscapeDataString($nativeLog)
        $nativeLoggingValue = if ($NativeLogging) { 1 } else { 0 }
        Send-Command $pipeName "load instance=MULTIV_FMU_WARPPER unzip=$unzip logging=$nativeLoggingValue log=$logValue" 10000 | Out-Null
        $toleranceText = $Tolerance.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
        Send-Command $pipeName "setup instance=MULTIV_FMU_WARPPER start=0 stop=0 hasStop=0 tolerance=$toleranceText toleranceDefined=1" 10000 | Out-Null
        Send-Command $pipeName "enter instance=MULTIV_FMU_WARPPER" 10000 | Out-Null

        Set-Reals $pipeName (New-ProductInputs 1970 $roomTemperatures)
        Send-Command $pipeName "exit instance=MULTIV_FMU_WARPPER" $TimeoutMs | Out-Null

        $lastMainEev = 1970.0
        $count = [Math]::Ceiling($DurationSeconds / $stepSize)
        for ($stepIndex = 0; $stepIndex -lt $count; $stepIndex++) {
            $current = $stepIndex * $stepSize
            $step = [Math]::Min($stepSize, $DurationSeconds - $current)
            $mainEev = Get-ReplayMainEev $current
            $atMacroBoundary = [Math]::Abs($current - [Math]::Round($current)) -lt 1.0e-9
            if ($RepeatInputsEachSecond -and $atMacroBoundary) {
                Set-Reals $pipeName (New-ProductInputs $mainEev $roomTemperatures)
                $lastMainEev = $mainEev
            }
            elseif ($mainEev -ne $lastMainEev) {
                Set-Reals $pipeName ([ordered]@{ MAIN_EEV_CurPulse = $mainEev })
                $lastMainEev = $mainEev
            }

            $stepWatch = [Diagnostics.Stopwatch]::StartNew()
            try {
                $currentText = $current.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
                $stepText = $step.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
                Send-Command $pipeName "step instance=MULTIV_FMU_WARPPER current=$currentText step=$stepText" $TimeoutMs | Out-Null
                $completedSteps++
            }
            catch {
                $result = if ($_.Exception.Message -match "timed out") { "TIMEOUT" } else { "ERROR" }
                $failureTime = $current
                $errorText = $_.Exception.Message
            }
            finally { $stepWatch.Stop() }

            if ($stepWatch.Elapsed.TotalMilliseconds -gt $maxStepMs) {
                $maxStepMs = $stepWatch.Elapsed.TotalMilliseconds
                $maxStepTime = $current
            }
            $stepRows.Add([pscustomobject]@{
                StepSize = $stepSize
                CurrentTime = $current
                MainEevPulse = $mainEev
                Result = $result
                ElapsedMilliseconds = [Math]::Round($stepWatch.Elapsed.TotalMilliseconds, 3)
                Error = $errorText
            })
            if ($result -ne "OK") { break }
        }

        if ($result -eq "OK") {
            $finalR1MassFlow = Get-Real $pipeName "IDU_01_Air_mfr_Discharge"
            $finalR2MassFlow = Get-Real $pipeName "IDU_02_Air_mfr_Discharge"
        }
    }
    catch {
        $result = if ($_.Exception.Message -match "timed out") { "TIMEOUT" } else { "ERROR" }
        $errorText = $_.Exception.Message
    }
    finally {
        $runWatch.Stop()
        try { Send-Command $pipeName "shutdown" 2000 | Out-Null } catch {}
        Close-PersistentPipe
        try {
            if (-not $process.HasExited) {
                $process.Kill()
                $process.WaitForExit(3000) | Out-Null
            }
        }
        catch { Write-Warning "Could not stop Product host: $($_.Exception.Message)" }
        $process.Dispose()
    }

    $summaryRows.Add([pscustomobject]@{
        StepSize = $stepSize
        Tolerance = $Tolerance
        PersistentPipe = [bool]$UsePersistentPipe
        BatchIo = [bool]$UseBatchIo
        SkipUnchangedInputs = [bool]$SkipUnchangedInputs
        NativeLogging = [bool]$NativeLogging
        StepsPerSecond = 1.0 / $stepSize
        DurationTarget = $DurationSeconds
        Result = $result
        CompletedSteps = $completedSteps
        CompletedTime = $completedSteps * $stepSize
        FailureTime = $failureTime
        MaxStepMilliseconds = [Math]::Round($maxStepMs, 3)
        MaxStepAtTime = $maxStepTime
        TotalWallSeconds = [Math]::Round($runWatch.Elapsed.TotalSeconds, 3)
        FinalR1MassFlow = $finalR1MassFlow
        FinalR2MassFlow = $finalR2MassFlow
        Error = $errorText
    })
    Write-Output "h=$stepSize result=$result completed=$completedSteps failureTime=$failureTime maxStepMs=$([Math]::Round($maxStepMs, 1)) wallSeconds=$([Math]::Round($runWatch.Elapsed.TotalSeconds, 1))"
}

$summaryRows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
$stepRows | Export-Csv -LiteralPath $stepOutputPath -NoTypeInformation -Encoding UTF8
$summaryRows | Format-Table -AutoSize
Write-Output "SUMMARY_CSV=$OutputPath"
Write-Output "STEPS_CSV=$stepOutputPath"
if ($summaryRows.Result -contains "TIMEOUT" -or $summaryRows.Result -contains "ERROR") { exit 2 }
