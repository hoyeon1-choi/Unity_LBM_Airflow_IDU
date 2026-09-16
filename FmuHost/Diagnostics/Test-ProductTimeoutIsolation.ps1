param(
    [string[]]$Scenarios = @(
        "SafeReference",
        "IntegratedVectorMain100",
        "SafeVectorMain1970",
        "IntegratedVectorMain1970",
        "IntegratedMainReplay"
    ),
    [double]$DurationSeconds = 4.0,
    [double]$StepSizeSeconds = 0.1,
    [int]$TimeoutMs = 30000,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$Scenarios = @($Scenarios | ForEach-Object { $_ -split ',' })
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$hostExe = Join-Path $projectRoot "FmuHost\bin\Debug\net8.0\FmuHost.exe"
$plugin = Join-Path $projectRoot "Assets\Plugins\x86_64\FmuNativePlugin.dll"
$fmuPath = Join-Path $projectRoot "Assets\StreamingAssets\FMU\product\MULTIV_FMU_WARPPER.fmu"
$cacheRoot = Join-Path $projectRoot "Temp\CoSimulationTests\ProductIsolationCache"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot "Temp\CoSimulationTests\product_timeout_isolation_summary.csv"
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

$hash = (Get-FileHash -LiteralPath $fmuPath -Algorithm SHA256).Hash.Substring(0, 16)
$cache = Join-Path $cacheRoot "MULTIV_FMU_WARPPER_$hash"
if (-not (Test-Path -LiteralPath (Join-Path $cache "modelDescription.xml"))) {
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($fmuPath, $cache)
}

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

function Set-Real([string]$pipeName, [string]$name, [double]$value) {
    $text = $value.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
    Send-Command $pipeName "set instance=MULTIV_FMU_WARPPER name=$name value=$text" 5000 | Out-Null
}

function Get-ScenarioConfig([string]$scenario) {
    switch ($scenario) {
        "SafeReference" {
            return [pscustomobject]@{ InputVector="SafeReference"; FanRpm=4.0; FanMode=4.0; IduPulse=10.0; AirTemperature=30.0; VaryTemperature=$false; InitialRh=50.0; LaterRh=50.0; MainMode="Constant"; MainValue=10.0 }
        }
        "IntegratedVectorMain100" {
            return [pscustomobject]@{ InputVector="Integrated"; FanRpm=0.0; FanMode=0.0; IduPulse=0.0; AirTemperature=20.0; VaryTemperature=$true; InitialRh=40.0; LaterRh=100.0; MainMode="Constant"; MainValue=100.0 }
        }
        "SafeVectorMain1970" {
            return [pscustomobject]@{ InputVector="SafeReference"; FanRpm=4.0; FanMode=4.0; IduPulse=10.0; AirTemperature=30.0; VaryTemperature=$false; InitialRh=50.0; LaterRh=50.0; MainMode="Constant"; MainValue=1970.0 }
        }
        "IntegratedVectorMain1970" {
            return [pscustomobject]@{ InputVector="Integrated"; FanRpm=0.0; FanMode=0.0; IduPulse=0.0; AirTemperature=20.0; VaryTemperature=$true; InitialRh=40.0; LaterRh=100.0; MainMode="Constant"; MainValue=1970.0 }
        }
        "IntegratedMainReplay" {
            return [pscustomobject]@{ InputVector="Integrated"; FanRpm=0.0; FanMode=0.0; IduPulse=0.0; AirTemperature=20.0; VaryTemperature=$true; InitialRh=40.0; LaterRh=100.0; MainMode="Replay"; MainValue=1970.0 }
        }
        "IntegratedNoHumidityJump" {
            return [pscustomobject]@{ InputVector="IntegratedNoHumidityJump"; FanRpm=0.0; FanMode=0.0; IduPulse=0.0; AirTemperature=20.0; VaryTemperature=$true; InitialRh=40.0; LaterRh=40.0; MainMode="Replay"; MainValue=1970.0 }
        }
        "SafeVectorHumidity100" {
            return [pscustomobject]@{ InputVector="SafeVectorHumidity100"; FanRpm=4.0; FanMode=4.0; IduPulse=10.0; AirTemperature=30.0; VaryTemperature=$false; InitialRh=50.0; LaterRh=100.0; MainMode="Constant"; MainValue=10.0 }
        }
        "IntegratedHumidity99" {
            return [pscustomobject]@{ InputVector="IntegratedHumidity99"; FanRpm=0.0; FanMode=0.0; IduPulse=0.0; AirTemperature=20.0; VaryTemperature=$true; InitialRh=40.0; LaterRh=99.0; MainMode="Replay"; MainValue=1970.0 }
        }
        "SafeVectorFanZero" {
            return [pscustomobject]@{ InputVector="SafeVectorFanZero"; FanRpm=0.0; FanMode=0.0; IduPulse=10.0; AirTemperature=30.0; VaryTemperature=$false; InitialRh=50.0; LaterRh=50.0; MainMode="Constant"; MainValue=10.0 }
        }
        "SafeVectorOduFanRpmZero" {
            return [pscustomobject]@{ InputVector="SafeVectorOduFanRpmZero"; FanRpm=0.0; FanMode=4.0; IduPulse=10.0; AirTemperature=30.0; VaryTemperature=$false; InitialRh=50.0; LaterRh=50.0; MainMode="Constant"; MainValue=10.0 }
        }
        "SafeVectorIduFanModeZero" {
            return [pscustomobject]@{ InputVector="SafeVectorIduFanModeZero"; FanRpm=4.0; FanMode=0.0; IduPulse=10.0; AirTemperature=30.0; VaryTemperature=$false; InitialRh=50.0; LaterRh=50.0; MainMode="Constant"; MainValue=10.0 }
        }
        "SafeVectorIduPulseZero" {
            return [pscustomobject]@{ InputVector="SafeVectorIduPulseZero"; FanRpm=4.0; FanMode=4.0; IduPulse=0.0; AirTemperature=30.0; VaryTemperature=$false; InitialRh=50.0; LaterRh=50.0; MainMode="Constant"; MainValue=10.0 }
        }
        "SafeVectorTemperature20" {
            return [pscustomobject]@{ InputVector="SafeVectorTemperature20"; FanRpm=4.0; FanMode=4.0; IduPulse=10.0; AirTemperature=20.0; VaryTemperature=$true; InitialRh=50.0; LaterRh=50.0; MainMode="Constant"; MainValue=10.0 }
        }
        default { throw "Unknown scenario: $scenario" }
    }
}

function Get-ReplayMainEev([double]$time) {
    if ($time -lt 1.0) { return 1970.0 }
    if ($time -lt 2.0) { return 1790.0 }
    if ($time -lt 3.0) { return 1670.0 }
    return 1550.0
}

$summaryRows = [Collections.Generic.List[object]]::new()
$stepRows = [Collections.Generic.List[object]]::new()

foreach ($scenario in $Scenarios) {
    $config = Get-ScenarioConfig $scenario
    $pipeName = "product_isolation_" + [Guid]::NewGuid().ToString("N")
    $hostLog = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) "product_isolation_${scenario}.host.log"
    $nativeLog = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) "product_isolation_${scenario}.native.log"
    $process = Start-Process -FilePath $hostExe -ArgumentList @(
        "--pipe", $pipeName, "--plugin", $plugin, "--log", $hostLog
    ) -PassThru -WindowStyle Hidden -WorkingDirectory $cache

    $result = "OK"
    $errorText = ""
    $completedSubsteps = 0
    $failureTime = $null
    $maxStepMs = 0.0
    $scenarioWatch = [Diagnostics.Stopwatch]::StartNew()

    try {
        foreach ($attempt in 1..30) {
            try { Send-Command $pipeName "ping" 500 | Out-Null; break }
            catch { if ($attempt -eq 30) { throw }; Start-Sleep -Milliseconds 100 }
        }

        $unzip = [Uri]::EscapeDataString($cache)
        $logValue = [Uri]::EscapeDataString($nativeLog)
        Send-Command $pipeName "load instance=MULTIV_FMU_WARPPER unzip=$unzip logging=1 log=$logValue" 10000 | Out-Null
        Send-Command $pipeName "setup instance=MULTIV_FMU_WARPPER start=0 stop=0 hasStop=0 tolerance=0.0001 toleranceDefined=1" 10000 | Out-Null
        Send-Command $pipeName "enter instance=MULTIV_FMU_WARPPER" 10000 | Out-Null

        Set-Real $pipeName "Comp_CurFreq" 0
        Set-Real $pipeName "Fan_CurRPM" $config.FanRpm
        Set-Real $pipeName "reversing_valve_mode_flag" 0
        Set-Real $pipeName "MAIN_EEV_CurPulse" $config.MainValue
        foreach ($index in 1..5) {
            $prefix = "idu_{0:D2}" -f $index
            Set-Real $pipeName "${prefix}_onoff" 1
            Set-Real $pipeName "${prefix}_fan_mode" $config.FanMode
            Set-Real $pipeName "${prefix}_pulse" $config.IduPulse
            Set-Real $pipeName "${prefix}_temp_air" $config.AirTemperature
            Set-Real $pipeName "${prefix}_RH_air" $config.InitialRh
        }
        Send-Command $pipeName "exit instance=MULTIV_FMU_WARPPER" $TimeoutMs | Out-Null

        $count = [Math]::Ceiling($DurationSeconds / $StepSizeSeconds)
        for ($stepIndex = 0; $stepIndex -lt $count; $stepIndex++) {
            $current = $stepIndex * $StepSizeSeconds
            $step = [Math]::Min($StepSizeSeconds, $DurationSeconds - $current)
            $mainEev = if ($config.MainMode -eq "Replay") { Get-ReplayMainEev $current } else { $config.MainValue }
            $temperature = $config.AirTemperature
            if ($config.VaryTemperature) {
                $temperature += 0.0039208 * [Math]::Floor($current)
            }
            $rh = if ($current -ge 1.0) { $config.LaterRh } else { $config.InitialRh }

            Set-Real $pipeName "MAIN_EEV_CurPulse" $mainEev
            foreach ($index in 1..5) {
                $prefix = "idu_{0:D2}" -f $index
                Set-Real $pipeName "${prefix}_temp_air" $temperature
                Set-Real $pipeName "${prefix}_RH_air" $rh
            }

            $stepWatch = [Diagnostics.Stopwatch]::StartNew()
            try {
                $currentText = $current.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
                $stepText = $step.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
                Send-Command $pipeName "step instance=MULTIV_FMU_WARPPER current=$currentText step=$stepText" $TimeoutMs | Out-Null
                $completedSubsteps++
            }
            catch {
                $result = if ($_.Exception.Message -match "timed out") { "TIMEOUT" } else { "ERROR" }
                $failureTime = $current
                $errorText = $_.Exception.Message
            }
            finally { $stepWatch.Stop() }

            $maxStepMs = [Math]::Max($maxStepMs, $stepWatch.Elapsed.TotalMilliseconds)
            $stepRows.Add([pscustomobject]@{
                Scenario = $scenario
                CurrentTime = $current
                MainEevPulse = $mainEev
                FanMode = $config.FanMode
                IduEevPulse = $config.IduPulse
                AirTemperature = $temperature
                RelativeHumidity = $rh
                Result = if ($result -eq "OK") { "OK" } else { $result }
                ElapsedMilliseconds = [Math]::Round($stepWatch.Elapsed.TotalMilliseconds, 3)
                Error = $errorText
            })
            if ($result -ne "OK") { break }
        }
    }
    catch {
        $result = if ($_.Exception.Message -match "timed out") { "TIMEOUT" } else { "ERROR" }
        $errorText = $_.Exception.Message
    }
    finally {
        $scenarioWatch.Stop()
        try { Send-Command $pipeName "shutdown" 2000 | Out-Null } catch {}
        try {
            if (-not $process.HasExited) {
                $process.Kill()
                $process.WaitForExit(3000) | Out-Null
            }
        }
        catch { Write-Warning "Could not stop $scenario host: $($_.Exception.Message)" }
        $process.Dispose()
    }

    $summaryRows.Add([pscustomobject]@{
        Scenario = $scenario
        OtherInputVector = $config.InputVector
        MainEevMode = $config.MainMode
        MainEevInitial = $config.MainValue
        Result = $result
        CompletedSubsteps = $completedSubsteps
        FailureTime = $failureTime
        MaxStepMilliseconds = [Math]::Round($maxStepMs, 3)
        TotalSeconds = [Math]::Round($scenarioWatch.Elapsed.TotalSeconds, 3)
        Error = $errorText
    })
    Write-Output "$scenario result=$result completed=$completedSubsteps failureTime=$failureTime maxStepMs=$([Math]::Round($maxStepMs, 1))"
}

$summaryRows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
$stepRows | Export-Csv -LiteralPath $stepOutputPath -NoTypeInformation -Encoding UTF8
$summaryRows | Format-Table -AutoSize
Write-Output "SUMMARY_CSV=$OutputPath"
Write-Output "STEPS_CSV=$stepOutputPath"
if ($summaryRows.Result -contains "TIMEOUT" -or $summaryRows.Result -contains "ERROR") { exit 2 }
