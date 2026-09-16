param(
    [int]$StepCount = 10,
    [double]$CommunicationStepSeconds = 1.0,
    [double]$ProductSubstepSeconds = 0.1,
    [double]$SetFan = 4.0,
    [int]$TimeoutMs = 30000,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$hostExe = Join-Path $projectRoot "FmuHost\bin\Debug\net8.0\FmuHost.exe"
$plugin = Join-Path $projectRoot "Assets\Plugins\x86_64\FmuNativePlugin.dll"
$fmuRoot = Join-Path $projectRoot "Assets\StreamingAssets\FMU"
$cacheRoot = Join-Path $projectRoot "Temp\CoSimulationTests\ChainCache"

if (-not (Test-Path -LiteralPath $hostExe)) { throw "FmuHost was not built: $hostExe" }
if ($StepCount -lt 1) { throw "StepCount must be at least 1." }
if ($CommunicationStepSeconds -le 0 -or $ProductSubstepSeconds -le 0) { throw "Step sizes must be positive." }
if ($SetFan -lt 1 -or $SetFan -gt 5) {
    throw "SetFan must be in the operating range 1..5 because this test turns all indoor units on."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot "Temp\CoSimulationTests\controller_product_plant_chain.csv"
}
elseif (-not [IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $projectRoot $OutputPath
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Expand-Fmu([string]$relativePath, [string]$modelId) {
    $fmuPath = Join-Path $fmuRoot $relativePath
    if (-not (Test-Path -LiteralPath $fmuPath)) { throw "FMU was not found: $fmuPath" }
    $hash = (Get-FileHash -LiteralPath $fmuPath -Algorithm SHA256).Hash.Substring(0, 16)
    $cache = Join-Path $cacheRoot "${modelId}_$hash"
    if (-not (Test-Path -LiteralPath (Join-Path $cache "modelDescription.xml"))) {
        New-Item -ItemType Directory -Force -Path $cache | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory($fmuPath, $cache)
    }
    return $cache
}

function Send-HostCommand([object]$hostInfo, [string]$request, [int]$timeout = $TimeoutMs) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(".", $hostInfo.PipeName, [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect($timeout)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 4096, $true)
        try {
            $writer.AutoFlush = $true
            $writer.WriteLine($request)
            $task = $reader.ReadLineAsync()
            if (-not $task.Wait($timeout)) { throw "[$($hostInfo.ModelId)] command timed out: $request" }
            $response = $task.GetAwaiter().GetResult()
            if ($null -eq $response -or -not $response.StartsWith("ok=1")) {
                throw "[$($hostInfo.ModelId)] command failed: request=$request response=$response"
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

function Start-FmuHost([string]$modelId, [string]$cache) {
    $pipeName = "chain_" + $modelId + "_" + [Guid]::NewGuid().ToString("N")
    $hostLog = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) "$modelId.chain.host.log"
    $process = Start-Process -FilePath $hostExe -ArgumentList @(
        "--pipe", $pipeName, "--plugin", $plugin, "--log", $hostLog
    ) -PassThru -WindowStyle Hidden -WorkingDirectory $cache
    $hostInfo = [pscustomobject]@{ ModelId=$modelId; PipeName=$pipeName; Process=$process; Cache=$cache }
    foreach ($attempt in 1..30) {
        try { Send-HostCommand $hostInfo "ping" 500 | Out-Null; return $hostInfo }
        catch { if ($attempt -eq 30) { throw }; Start-Sleep -Milliseconds 100 }
    }
}

function Load-Fmu([object]$hostInfo, [bool]$isController = $false) {
    $unzip = [Uri]::EscapeDataString($hostInfo.Cache)
    $nativeLog = [Uri]::EscapeDataString((Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) "$($hostInfo.ModelId).chain.native.log"))
    Send-HostCommand $hostInfo "load instance=$($hostInfo.ModelId) unzip=$unzip logging=0 log=$nativeLog" $TimeoutMs | Out-Null

    if ($isController) {
        Send-HostCommand $hostInfo "register instance=$($hostInfo.ModelId) name=Period value=1" | Out-Null
        Send-HostCommand $hostInfo "register instance=$($hostInfo.ModelId) name=Multi_V_S.TotalIDUNum value=5" | Out-Null
        foreach ($index in 1..5) {
            $prefix = "IDU_{0:D2}" -f $index
            Send-HostCommand $hostInfo "registerInteger instance=$($hostInfo.ModelId) name=${prefix}.Type value=1" | Out-Null
            Send-HostCommand $hostInfo "register instance=$($hostInfo.ModelId) name=${prefix}.IDU_Address value=$index" | Out-Null
            $iduHex = [Uri]::EscapeDataString((Join-Path $fmuRoot "Korea_MultiV_CST_Main_EEPROM_24C16_RNW0721C2S_SAA43756039_001_4DDC_0x03F4B670.hex").Replace('\', '/'))
            Send-HostCommand $hostInfo "registerString instance=$($hostInfo.ModelId) name=${prefix}.Option_HEX_path value=$iduHex" | Out-Null
        }
        $oduHex = [Uri]::EscapeDataString((Join-Path $fmuRoot "S_SAA37571716_RPUW100S9S_141016_0456.hex").Replace('\', '/'))
        Send-HostCommand $hostInfo "registerString instance=$($hostInfo.ModelId) name=Multi_V_S.Option_HEX_path value=$oduHex" | Out-Null
    }

    Send-HostCommand $hostInfo "setup instance=$($hostInfo.ModelId) start=0 stop=0 hasStop=0 tolerance=0.0001 toleranceDefined=1" | Out-Null
    Send-HostCommand $hostInfo "enter instance=$($hostInfo.ModelId)" | Out-Null
}

function Exit-Initialization([object]$hostInfo) {
    Send-HostCommand $hostInfo "exit instance=$($hostInfo.ModelId)" $TimeoutMs | Out-Null
}

function Set-Real([object]$hostInfo, [string]$name, [double]$value) {
    $number = $value.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
    Send-HostCommand $hostInfo "set instance=$($hostInfo.ModelId) name=$name value=$number" | Out-Null
}

function Get-Real([object]$hostInfo, [string]$name) {
    $response = Send-HostCommand $hostInfo "get instance=$($hostInfo.ModelId) name=$name"
    if ($response -notmatch '(?:^|\s)value=([^\s]+)') { throw "Value missing in response: $response" }
    return [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
}

function Step-Fmu([object]$hostInfo, [double]$current, [double]$step) {
    $currentText = $current.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
    $stepText = $step.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
    Send-HostCommand $hostInfo "step instance=$($hostInfo.ModelId) current=$currentText step=$stepText" $TimeoutMs | Out-Null
}

function Assert-Finite([double]$value, [string]$signal) {
    if ([double]::IsNaN($value) -or [double]::IsInfinity($value)) { throw "Non-finite signal: $signal=$value" }
}

$hosts = [Collections.Generic.List[object]]::new()
$rows = [Collections.Generic.List[object]]::new()
$serverProcess = $null
$failure = ""
$activeStepNumber = 0
$activeStage = "Initialization"

Write-Warning "Cooling-mode profile: the unavailable Multi_V_S.4Way_Valve__OnOff connection is disabled; reversing_valve_mode_flag=0 is used."

try {
    $controllerCache = Expand-Fmu "controller\Multi_V_S__Set_CFMU.fmu" "Multi_V_S__Set_CFMU"
    $productCache = Expand-Fmu "product\MULTIV_FMU_WARPPER.fmu" "MULTIV_FMU_WARPPER"
    $chamberCaches = @{}
    foreach ($index in 2..5) {
        $modelId = "Simple_Chamber_R$index"
        $chamberCaches[$index] = Expand-Fmu "plant\$modelId.fmu" $modelId
    }

    $iduHexPath = (Join-Path $fmuRoot "Korea_MultiV_CST_Main_EEPROM_24C16_RNW0721C2S_SAA43756039_001_4DDC_0x03F4B670.hex").Replace('\', '/')
    $oduHexPath = (Join-Path $fmuRoot "S_SAA37571716_RPUW100S9S_141016_0456.hex").Replace('\', '/')
    $controllerDescriptionPath = Join-Path $controllerCache "modelDescription.xml"
    [xml]$controllerDescription = Get-Content -LiteralPath $controllerDescriptionPath
    foreach ($scalar in $controllerDescription.fmiModelDescription.ModelVariables.ScalarVariable) {
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
            $scalar.SelectSingleNode('./String').SetAttribute('start', $startValue)
        }
    }
    $controllerDescription.Save($controllerDescriptionPath)

    # The controller wrapper requires its bundled server. Initialization stays sequential.
    $serverPath = Join-Path $controllerCache "resources\binaries\win64\FMI2CoSimulationServer.exe"
    $serverProcess = Start-Process -FilePath $serverPath -PassThru -WindowStyle Hidden -WorkingDirectory $controllerCache
    Start-Sleep -Milliseconds 500

    $controller = Start-FmuHost "Multi_V_S__Set_CFMU" $controllerCache
    $hosts.Add($controller)
    Load-Fmu $controller $true

    $product = Start-FmuHost "MULTIV_FMU_WARPPER" $productCache
    $hosts.Add($product)
    Load-Fmu $product

    $chambers = @{}
    foreach ($index in 2..5) {
        $modelId = "Simple_Chamber_R$index"
        $chamber = Start-FmuHost $modelId $chamberCaches[$index]
        $hosts.Add($chamber)
        Load-Fmu $chamber
        $chambers[$index] = $chamber
    }

    $roomTemperature = @{1=20.0;2=20.0;3=20.0;4=20.0;5=20.0}
    $roomHumidity = @{1=40.0;2=40.0;3=40.0;4=40.0;5=40.0}
    $pipeIn = @{1=30.4019;2=30.4020;3=30.4020;4=30.2354;5=30.2354}
    $pipeOut = @{1=26.2016;2=26.1984;3=26.1984;4=22.5997;5=22.5997}
    $oduFeedback = [ordered]@{
        "Multi_V_S.Sensor__Pressure_HI" = 1398.41
        "Multi_V_S.Sensor__Pressure_LO" = 1411.76
        "Multi_V_S.Sensor__Temp_SC_Out" = 1.0
        "Multi_V_S.Sensor__Temp_SC_In" = 1.0
        "Multi_V_S.Sensor__Temp_OutAir" = 35.0
        "Multi_V_S.Sensor__Temp_Liquid" = 21.4070
        "Multi_V_S.Sensor__Temp_HEXPipe" = 21.3259
        "Multi_V_S.Sensor__Temp_Discharge" = 21.4069
        "Multi_V_S.Sensor__Temp_Suction" = 21.7393
    }

    foreach ($index in 1..5) {
        $prefix = "IDU_{0:D2}" -f $index
        Set-Real $controller "${prefix}.FOnOff" 1
        Set-Real $controller "${prefix}.SetMode" 0
        Set-Real $controller "${prefix}.SetTemp" 28
        Set-Real $controller "${prefix}.SetFan" $SetFan
        Set-Real $controller "${prefix}.Room_Temp" $roomTemperature[$index]
        Set-Real $controller "${prefix}.Pipe_In_Temp" $pipeIn[$index]
        Set-Real $controller "${prefix}.Pipe_Out_Temp" $pipeOut[$index]
        Set-Real $controller "${prefix}.Humidity" 40

        $productPrefix = "idu_{0:D2}" -f $index
        Set-Real $product "${productPrefix}_onoff" 1
        Set-Real $product "${productPrefix}_fan_mode" $SetFan
        Set-Real $product "${productPrefix}_pulse" 0
        Set-Real $product "${productPrefix}_temp_air" $roomTemperature[$index]
        Set-Real $product "${productPrefix}_RH_air" $roomHumidity[$index]
    }
    foreach ($entry in $oduFeedback.GetEnumerator()) { Set-Real $controller $entry.Key $entry.Value }
    Set-Real $product "Comp_CurFreq" 0
    Set-Real $product "Fan_CurRPM" 0
    Set-Real $product "reversing_valve_mode_flag" 0
    Set-Real $product "MAIN_EEV_CurPulse" 10
    foreach ($index in 2..5) {
        Set-Real $chambers[$index] "T_air_dis" 33
        Set-Real $chambers[$index] "RH_air_dis" 70
        Set-Real $chambers[$index] "mfr_air_dis" 0.98
        Set-Real $chambers[$index] "mfr_leakage" 0.01
    }

    Exit-Initialization $controller
    Exit-Initialization $product
    foreach ($index in 2..5) { Exit-Initialization $chambers[$index] }

    for ($macroStep = 0; $macroStep -lt $StepCount; $macroStep++) {
        $time = $macroStep * $CommunicationStepSeconds
$activeStepNumber = $macroStep + 1

        # Previous Product/Plant outputs and profile constants are applied first.
$activeStage = "Controller"
        foreach ($index in 1..5) {
            $prefix = "IDU_{0:D2}" -f $index
            Set-Real $controller "${prefix}.FOnOff" 1
            Set-Real $controller "${prefix}.SetMode" 0
            Set-Real $controller "${prefix}.SetTemp" 28
            Set-Real $controller "${prefix}.SetFan" $SetFan
            Set-Real $controller "${prefix}.Room_Temp" $roomTemperature[$index]
            Set-Real $controller "${prefix}.Pipe_In_Temp" $pipeIn[$index]
            Set-Real $controller "${prefix}.Pipe_Out_Temp" $pipeOut[$index]
            Set-Real $controller "${prefix}.Humidity" 40
        }
        foreach ($entry in $oduFeedback.GetEnumerator()) { Set-Real $controller $entry.Key $entry.Value }

        $controllerWatch = [Diagnostics.Stopwatch]::StartNew()
        Step-Fmu $controller $time $CommunicationStepSeconds
        $controllerWatch.Stop()

        $compHz = Get-Real $controller "Multi_V_S.Comp__CurFreq"
        $fanRpm = Get-Real $controller "Multi_V_S.Fan1__CurRPM"
        $mainEev = Get-Real $controller "Multi_V_S.Main_EEV__TarPulse"
        $fanModes = @{}
        $iduPulses = @{}
        foreach ($index in 1..5) {
            $prefix = "IDU_{0:D2}" -f $index
            $fanModes[$index] = Get-Real $controller "${prefix}.CurSetFan"
            $iduPulses[$index] = Get-Real $controller "${prefix}.EEV_TarPulse"
        }

        # New controller package has no 4Way_Valve__OnOff output; cooling-mode zero is explicit.
$activeStage = "ControllerToProductTransfer"
        Set-Real $product "Comp_CurFreq" $compHz
        Set-Real $product "Fan_CurRPM" $fanRpm
        Set-Real $product "reversing_valve_mode_flag" 0
        Set-Real $product "MAIN_EEV_CurPulse" $mainEev
        foreach ($index in 1..5) {
            $prefix = "idu_{0:D2}" -f $index
            Set-Real $product "${prefix}_onoff" 1
            Set-Real $product "${prefix}_fan_mode" $fanModes[$index]
            Set-Real $product "${prefix}_pulse" $iduPulses[$index]
            Set-Real $product "${prefix}_temp_air" $roomTemperature[$index]
            Set-Real $product "${prefix}_RH_air" ([Math]::Max(0, [Math]::Min(100, $roomHumidity[$index])))
        }

        $transferError = [Math]::Abs((Get-Real $product "Comp_CurFreq") - $compHz)
        foreach ($index in 1..5) {
            $prefix = "idu_{0:D2}" -f $index
            $transferError = [Math]::Max($transferError, [Math]::Abs((Get-Real $product "${prefix}_pulse") - $iduPulses[$index]))
        }

        $productWatch = [Diagnostics.Stopwatch]::StartNew()
$activeStage = "Product"
        $productMaxSubstepMs = 0.0
        $substepCount = [Math]::Ceiling($CommunicationStepSeconds / $ProductSubstepSeconds)
        for ($substep = 0; $substep -lt $substepCount; $substep++) {
            $substepTime = $time + ($substep * $ProductSubstepSeconds)
            $substepSize = [Math]::Min($ProductSubstepSeconds, ($time + $CommunicationStepSeconds) - $substepTime)
            $substepWatch = [Diagnostics.Stopwatch]::StartNew()
            Step-Fmu $product $substepTime $substepSize
            $substepWatch.Stop()
            $productMaxSubstepMs = [Math]::Max($productMaxSubstepMs, $substepWatch.Elapsed.TotalMilliseconds)
        }
        $productWatch.Stop()

        $oduFeedback["Multi_V_S.Sensor__Pressure_HI"] = Get-Real $product "ODU_Sensor_Pressure_HI"
        $oduFeedback["Multi_V_S.Sensor__Pressure_LO"] = Get-Real $product "ODU_Sensor_Pressure_LO"
        $oduFeedback["Multi_V_S.Sensor__Temp_SC_Out"] = Get-Real $product "ODU_Sensor_Temp_SC_Out"
        $oduFeedback["Multi_V_S.Sensor__Temp_SC_In"] = Get-Real $product "ODU_Sensor_Temp_SC_In"
        $oduFeedback["Multi_V_S.Sensor__Temp_OutAir"] = 35.0
        $oduFeedback["Multi_V_S.Sensor__Temp_Liquid"] = Get-Real $product "ODU_Sensor_Temp_Liquid"
        $oduFeedback["Multi_V_S.Sensor__Temp_HEXPipe"] = Get-Real $product "ODU_Sensor_Temp_HEXPipe"
        $oduFeedback["Multi_V_S.Sensor__Temp_Discharge"] = Get-Real $product "ODU_Sensor_Temp_Discharge"
        $oduFeedback["Multi_V_S.Sensor__Temp_Suction"] = Get-Real $product "ODU_Sensor_Temp_Suction"

        foreach ($index in 1..5) {
            $pipeInName = if ($index -eq 2) { "IDU_02_Sensor_Temp_Pipe_In2" } else { "IDU_{0:D2}_Sensor_Temp_Pipe_In" -f $index }
            $pipeIn[$index] = Get-Real $product $pipeInName
            $pipeOut[$index] = Get-Real $product ("IDU_{0:D2}_Sensor_Temp_Pipe_Out" -f $index)
        }

        $plantWatch = [Diagnostics.Stopwatch]::StartNew()
$activeStage = "ProductToPlantTransferAndPlant"
        foreach ($index in 2..5) {
            $dischargeTemperature = Get-Real $product ("IDU_{0:D2}_Air_Temp_Discharge" -f $index)
            $dischargeRh = Get-Real $product ("IDU_{0:D2}_Air_RH_Discharge" -f $index)
            $dischargeMassFlow = Get-Real $product ("IDU_{0:D2}_Air_mfr_Discharge" -f $index)
            Assert-Finite $dischargeTemperature "IDU_${index}_Air_Temp_Discharge"
            Assert-Finite $dischargeRh "IDU_${index}_Air_RH_Discharge"
            Assert-Finite $dischargeMassFlow "IDU_${index}_Air_mfr_Discharge"
            Set-Real $chambers[$index] "T_air_dis" $dischargeTemperature
            Set-Real $chambers[$index] "RH_air_dis" $dischargeRh
            Set-Real $chambers[$index] "mfr_air_dis" $dischargeMassFlow
            Step-Fmu $chambers[$index] $time $CommunicationStepSeconds
            $roomTemperature[$index] = Get-Real $chambers[$index] "T_air_suc"
            $roomHumidity[$index] = Get-Real $chambers[$index] "RH_air_suc"
            Assert-Finite $roomTemperature[$index] "Simple_Chamber_R${index}.T_air_suc"
            Assert-Finite $roomHumidity[$index] "Simple_Chamber_R${index}.RH_air_suc"
        }
        $plantWatch.Stop()

        foreach ($value in @($compHz, $fanRpm, $mainEev) + @($oduFeedback.Values)) {
            Assert-Finite ([double]$value) "coupled output"
        }

        $rows.Add([pscustomobject]@{
            Step = $macroStep + 1
            TimeSeconds = $time + $CommunicationStepSeconds
            Order = "Controller>Product>PlantR2>PlantR3>PlantR4>PlantR5"
            ControllerMs = [Math]::Round($controllerWatch.Elapsed.TotalMilliseconds, 3)
            ProductMs = [Math]::Round($productWatch.Elapsed.TotalMilliseconds, 3)
            ProductMaxSubstepMs = [Math]::Round($productMaxSubstepMs, 3)
            PlantTotalMs = [Math]::Round($plantWatch.Elapsed.TotalMilliseconds, 3)
            TransferMaxAbsError = $transferError
            CompHz = $compHz
            FanRpm = $fanRpm
            MainEevPulse = $mainEev
            RequestedSetFan = $SetFan
            IDU01FanMode = $fanModes[1]
            IDU02FanMode = $fanModes[2]
            IDU03FanMode = $fanModes[3]
            IDU04FanMode = $fanModes[4]
            IDU05FanMode = $fanModes[5]
            IDU01EevPulse = $iduPulses[1]
            IDU02EevPulse = $iduPulses[2]
            IDU03EevPulse = $iduPulses[3]
            IDU04EevPulse = $iduPulses[4]
            IDU05EevPulse = $iduPulses[5]
            OduPressureHi = $oduFeedback["Multi_V_S.Sensor__Pressure_HI"]
            OduPressureLo = $oduFeedback["Multi_V_S.Sensor__Pressure_LO"]
            R2SuctionTemp = $roomTemperature[2]
            R2SuctionRhRaw = $roomHumidity[2]
            R3SuctionTemp = $roomTemperature[3]
            R3SuctionRhRaw = $roomHumidity[3]
            R4SuctionTemp = $roomTemperature[4]
            R4SuctionRhRaw = $roomHumidity[4]
            R5SuctionTemp = $roomTemperature[5]
            R5SuctionRhRaw = $roomHumidity[5]
            Status = "OK"
        })
        Write-Output ("step={0}/{1} order=Controller>Product>Plant transferError={2:R} controllerMs={3:F1} productMs={4:F1} plantMs={5:F1}" -f ($macroStep + 1), $StepCount, $transferError, $controllerWatch.Elapsed.TotalMilliseconds, $productWatch.Elapsed.TotalMilliseconds, $plantWatch.Elapsed.TotalMilliseconds)
    }
}
catch {
    $failure = $_.Exception.Message
    if ($activeStepNumber -gt 0 -and $rows.Count -lt $activeStepNumber) {
        $rows.Add([pscustomobject]@{
            Step = $activeStepNumber
            TimeSeconds = ($activeStepNumber - 1) * $CommunicationStepSeconds
            Order = "Controller>Product>PlantR2>PlantR3>PlantR4>PlantR5"
            ControllerMs = $null
            ProductMs = $null
            ProductMaxSubstepMs = $null
            PlantTotalMs = $null
            TransferMaxAbsError = $null
            CompHz = $null
            FanRpm = $null
            MainEevPulse = $null
            RequestedSetFan = $SetFan
            IDU01FanMode = $null
            IDU02FanMode = $null
            IDU03FanMode = $null
            IDU04FanMode = $null
            IDU05FanMode = $null
            IDU01EevPulse = $null
            IDU02EevPulse = $null
            IDU03EevPulse = $null
            IDU04EevPulse = $null
            IDU05EevPulse = $null
            OduPressureHi = $null
            OduPressureLo = $null
            R2SuctionTemp = $null
            R2SuctionRhRaw = $null
            R3SuctionTemp = $null
            R3SuctionRhRaw = $null
            R4SuctionTemp = $null
            R4SuctionRhRaw = $null
            R5SuctionTemp = $null
            R5SuctionRhRaw = $null
            Status = "FAILED at ${activeStage}: $failure"
        })
    }
    Write-Error $failure
}
finally {
    $rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
    for ($index = $hosts.Count - 1; $index -ge 0; $index--) {
        $hostInfo = $hosts[$index]
        try { Send-HostCommand $hostInfo "unload instance=$($hostInfo.ModelId)" 3000 | Out-Null } catch {}
        try { Send-HostCommand $hostInfo "shutdown" 3000 | Out-Null } catch {}
        try {
            if (-not $hostInfo.Process.WaitForExit(3000)) { $hostInfo.Process.Kill() }
        }
        catch { Write-Warning "Could not stop $($hostInfo.ModelId) host: $($_.Exception.Message)" }
        $hostInfo.Process.Dispose()
    }
    if ($null -ne $serverProcess) {
        try {
            if (-not $serverProcess.HasExited) {
                $serverProcess.Kill()
                $serverProcess.WaitForExit(3000) | Out-Null
            }
        }
        catch { Write-Warning "Bundled controller server cleanup was skipped: $($_.Exception.Message)" }
        $serverProcess.Dispose()
    }
}

Write-Output "RESULT_CSV=$OutputPath"
if (-not [string]::IsNullOrEmpty($failure)) { exit 2 }
