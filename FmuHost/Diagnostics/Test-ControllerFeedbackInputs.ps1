param(
    [int]$StepTimeoutMs = 5000,
    [string]$OutputPath = "",
    [switch]$LifecycleOnly,
    [switch]$EnableFmuLogging,
    [string]$InstanceName = "ControllerProbe",
    [int]$InterStepDelaySeconds = 0,
    [int]$HeartbeatIntervalSeconds = 0
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$hostExePath = Join-Path $projectRoot "FmuHost\bin\Debug\net8.0\FmuHost.exe"
$pluginPath = Join-Path $projectRoot "Assets\Plugins\x86_64\FmuNativePlugin.dll"
$fmuRoot = Join-Path $projectRoot "Assets\StreamingAssets\FMU"
$controllerFmuPath = Join-Path $fmuRoot "controller\Multi_V_S__Set_CFMU.fmu"
$cacheHash = (Get-FileHash -LiteralPath $controllerFmuPath -Algorithm SHA256).Hash.Substring(0, 16)
$controllerCachePath = Join-Path $projectRoot "Temp\CoSimulationTests\ControllerCache\$cacheHash"

if (-not (Test-Path -LiteralPath $controllerCachePath)) {
    New-Item -ItemType Directory -Path $controllerCachePath -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($controllerFmuPath, $controllerCachePath)
}
$controllerCache = Get-Item -LiteralPath $controllerCachePath

$iduHexPath = (Join-Path $fmuRoot "Korea_MultiV_CST_Main_EEPROM_24C16_RNW0721C2S_SAA43756039_001_4DDC_0x03F4B670.hex").Replace('\', '/')
$oduHexPath = (Join-Path $fmuRoot "S_SAA37571716_RPUW100S9S_141016_0456.hex").Replace('\', '/')
$modelDescriptionPath = Join-Path $controllerCache.FullName "modelDescription.xml"
[xml]$modelDescription = Get-Content -LiteralPath $modelDescriptionPath
foreach ($scalar in $modelDescription.fmiModelDescription.ModelVariables.ScalarVariable) {
    $parameterName = [string]$scalar.name
    $startValue = if ($parameterName -eq "Multi_V_S.Option_HEX_path") {
        $oduHexPath
    }
    elseif ($parameterName -match '^IDU_0[1-5]\.Option_HEX_path$') {
        $iduHexPath
    }
    else {
        $null
    }

    if ($null -ne $startValue) {
        $stringNode = $scalar.SelectSingleNode('./String')
        $stringNode.SetAttribute('start', $startValue)
    }
}
$modelDescription.Save($modelDescriptionPath)

if (-not (Test-Path -LiteralPath $hostExePath)) {
    throw "FmuHost.exe was not found: $hostExePath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputDirectory = Join-Path $projectRoot "Temp\CoSimulationTests"
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $OutputPath = Join-Path $outputDirectory "controller_feedback_input_probe.csv"
}

$initialInputs = [ordered]@{}
foreach ($index in 1..5) {
    $prefix = "IDU_{0:D2}" -f $index
    $initialInputs["${prefix}.FOnOff"] = 1.0
    $initialInputs["${prefix}.SetMode"] = 0.0
    $initialInputs["${prefix}.SetTemp"] = 28.0
    $initialInputs["${prefix}.SetFan"] = 0.0
    $initialInputs["${prefix}.Humidity"] = 40.0
}
$initialInputs["Multi_V_S.Sensor__Temp_OutAir"] = 35.0

$feedbackInputs = [ordered]@{
    "IDU_01.Room_Temp" = 20.0039
    "IDU_01.Pipe_In_Temp" = 30.4019
    "IDU_01.Pipe_Out_Temp" = 26.2016
    "IDU_02.Room_Temp" = 20.0039
    "IDU_02.Pipe_In_Temp" = 30.4020
    "IDU_02.Pipe_Out_Temp" = 26.1984
    "IDU_03.Room_Temp" = 20.0039
    "IDU_03.Pipe_In_Temp" = 30.4020
    "IDU_03.Pipe_Out_Temp" = 26.1984
    "IDU_04.Room_Temp" = 20.0039
    "IDU_04.Pipe_In_Temp" = 30.2354
    "IDU_04.Pipe_Out_Temp" = 22.5997
    "IDU_05.Room_Temp" = 20.0039
    "IDU_05.Pipe_In_Temp" = 30.2354
    "IDU_05.Pipe_Out_Temp" = 22.5997
    "Multi_V_S.Sensor__Pressure_HI" = 1398.41
    "Multi_V_S.Sensor__Pressure_LO" = 1411.76
    "Multi_V_S.Sensor__Temp_SC_Out" = 1.0
    "Multi_V_S.Sensor__Temp_SC_In" = 1.0
    "Multi_V_S.Sensor__Temp_Liquid" = 21.4070
    "Multi_V_S.Sensor__Temp_HEXPipe" = 21.3259
    "Multi_V_S.Sensor__Temp_Discharge" = 21.4069
    "Multi_V_S.Sensor__Temp_Suction" = 21.7393
}

$controllerOutputs = @(
    1..5 | ForEach-Object {
        "IDU_{0:D2}.CurSetFan" -f $_
        "IDU_{0:D2}.EEV_TarPulse" -f $_
    }
) + @(
    "Multi_V_S.Comp__TarFreq",
    "Multi_V_S.Fan1__TarRPM",
    "Multi_V_S.4Way_Valve__OnOff",
    "Multi_V_S.MAIN_EEV__CurPulse"
)

function Convert-ToRequestValue([string]$value) {
    return [Uri]::EscapeDataString($value)
}

function Send-HostCommand(
    [string]$pipeName,
    [string]$request,
    [int]$timeoutMs
) {
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
        ".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect($timeoutMs)
        $writer = [System.IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
        $reader = [System.IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 4096, $true)
        try {
            $writer.AutoFlush = $true
            $writer.WriteLine($request)
            $readTask = $reader.ReadLineAsync()
            if (-not $readTask.Wait($timeoutMs)) {
                throw [TimeoutException]::new("Host command timed out: $request")
            }

            $response = $readTask.GetAwaiter().GetResult()
            if ($null -eq $response -or -not $response.StartsWith("ok=1")) {
                throw "Host command failed: request=$request response=$response"
            }
            return $response
        }
        finally {
            $writer.Dispose()
            $reader.Dispose()
        }
    }
    finally {
        $pipe.Dispose()
    }
}

function Invoke-ControllerCase(
    [string]$caseName,
    [string]$variableName,
    [double]$value,
    [System.Collections.IDictionary]$inputSet = $null,
    [string[]]$readOutputs = $null,
    [int]$additionalSteps = 0
) {
    $pipeName = "controller_probe_" + [Guid]::NewGuid().ToString("N")
    $caseLog = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) ("controller_probe_" + $caseName + ".log")
    $nativeLog = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) ("controller_native_" + $caseName + ".log")
    $runDirectory = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) ("ControllerRuns\" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $hostProcess = Start-Process -FilePath $hostExePath -ArgumentList @(
        "--pipe", $pipeName,
        "--plugin", $pluginPath,
        "--log", $caseLog) -PassThru -WindowStyle Hidden -WorkingDirectory $runDirectory
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $outputValues = [Collections.Generic.List[string]]::new()

    try {
        $ready = $false
        foreach ($attempt in 1..20) {
            try {
                Send-HostCommand $pipeName "ping" 500 | Out-Null
                $ready = $true
                break
            }
            catch {
                Start-Sleep -Milliseconds 100
            }
        }
        if (-not $ready) {
            throw "FmuHost did not become ready."
        }

        $instance = $InstanceName
        $unzipValue = Convert-ToRequestValue $controllerCache.FullName
        $loggingValue = if ($EnableFmuLogging) { "1" } else { "0" }
        $nativeLogValue = Convert-ToRequestValue $nativeLog
        Send-HostCommand $pipeName "load instance=$instance unzip=$unzipValue logging=$loggingValue log=$nativeLogValue" 10000 | Out-Null
        Send-HostCommand $pipeName "register instance=$instance name=Period value=1" 2000 | Out-Null
        Send-HostCommand $pipeName "setup instance=$instance start=0 stop=0 hasStop=0 tolerance=0 toleranceDefined=0" 5000 | Out-Null
        Send-HostCommand $pipeName "enter instance=$instance" 5000 | Out-Null
        Send-HostCommand $pipeName "exit instance=$instance" $StepTimeoutMs | Out-Null

        foreach ($entry in $initialInputs.GetEnumerator()) {
            $number = $entry.Value.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
            Send-HostCommand $pipeName "set instance=$instance name=$($entry.Key) value=$number" 2000 | Out-Null
        }

        Send-HostCommand $pipeName "step instance=$instance current=0 step=1" $StepTimeoutMs | Out-Null
        if ($null -ne $readOutputs) {
            foreach ($outputName in $readOutputs) {
                $response = Send-HostCommand $pipeName "get instance=$instance name=$outputName" 2000
                $outputValues.Add("$outputName=$response")
            }
        }
        if (-not [string]::IsNullOrEmpty($variableName)) {
            $number = $value.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
            Send-HostCommand $pipeName "set instance=$instance name=$variableName value=$number" 2000 | Out-Null
        }
        if ($null -ne $inputSet) {
            foreach ($entry in $inputSet.GetEnumerator()) {
                $number = $entry.Value.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
                Send-HostCommand $pipeName "set instance=$instance name=$($entry.Key) value=$number" 2000 | Out-Null
            }
        }
        if ($InterStepDelaySeconds -gt 0) {
            if ($HeartbeatIntervalSeconds -gt 0) {
                $remainingDelay = $InterStepDelaySeconds
                while ($remainingDelay -gt 0) {
                    $sleepSeconds = [Math]::Min($HeartbeatIntervalSeconds, $remainingDelay)
                    Start-Sleep -Seconds $sleepSeconds
                    $remainingDelay -= $sleepSeconds
                    Send-HostCommand $pipeName "get instance=$instance name=Multi_V_S.Comp__TarFreq" 2000 | Out-Null
                }
            }
            else {
                Start-Sleep -Seconds $InterStepDelaySeconds
            }
        }
        Send-HostCommand $pipeName "step instance=$instance current=1 step=1" $StepTimeoutMs | Out-Null
        if ($additionalSteps -gt 0) {
            foreach ($stepIndex in 1..$additionalSteps) {
                $currentTime = 1 + $stepIndex
                Send-HostCommand $pipeName "step instance=$instance current=$currentTime step=1" $StepTimeoutMs | Out-Null
            }
        }
        Send-HostCommand $pipeName "unload instance=$instance" 5000 | Out-Null
        Send-HostCommand $pipeName "shutdown" 5000 | Out-Null
        if (-not $hostProcess.WaitForExit(3000)) {
            throw "FmuHost did not exit after graceful shutdown."
        }

        return [pscustomobject]@{
            Case = $caseName
            Variable = $variableName
            Value = if ([string]::IsNullOrEmpty($variableName)) { "" } else { $value }
            Result = "OK"
            ElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
            Outputs = $outputValues -join ";"
            Error = ""
        }
    }
    catch {
        return [pscustomobject]@{
            Case = $caseName
            Variable = $variableName
            Value = if ([string]::IsNullOrEmpty($variableName)) { "" } else { $value }
            Result = if ($_.Exception -is [TimeoutException] -or $_.Exception.Message -match "timed out") { "TIMEOUT" } else { "ERROR" }
            ElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
            Outputs = $outputValues -join ";"
            Error = $_.Exception.Message
        }
    }
    finally {
        if (-not $hostProcess.HasExited) {
            $hostProcess.Kill()
            $hostProcess.WaitForExit(3000) | Out-Null
        }
        $hostProcess.Dispose()
    }
}

$results = [Collections.Generic.List[object]]::new()
if ($LifecycleOnly) {
    $results.Add((Invoke-ControllerCase "native_lifecycle" "" 0.0 $feedbackInputs $controllerOutputs 1))
}
else {
    $baseline = Invoke-ControllerCase "baseline" "" 0.0
    $results.Add($baseline)

    if ($baseline.Result -eq "OK") {
        foreach ($entry in $feedbackInputs.GetEnumerator()) {
            $results.Add((Invoke-ControllerCase $entry.Key $entry.Key $entry.Value))
        }
        $results.Add((Invoke-ControllerCase "all_feedback_inputs" "" 0.0 $feedbackInputs))
    }
}

$results | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
$results | Format-Table Case, Value, Result, ElapsedSeconds, Error -AutoSize
Write-Output "RESULT_CSV=$OutputPath"

if ($results.Result -contains "TIMEOUT" -or $results.Result -contains "ERROR") {
    exit 2
}
