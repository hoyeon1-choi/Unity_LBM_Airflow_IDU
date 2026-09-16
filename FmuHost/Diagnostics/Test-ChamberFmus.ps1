param(
    [int[]]$ChamberIndices = @(1, 2, 3, 4, 5),
    [int]$StepCount = 3,
    [int]$TimeoutMs = 30000,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$hostExe = Join-Path $projectRoot "FmuHost\bin\Debug\net8.0\FmuHost.exe"
$plugin = Join-Path $projectRoot "Assets\Plugins\x86_64\FmuNativePlugin.dll"
$fmuRoot = Join-Path $projectRoot "Assets\StreamingAssets\FMU\plant"
$cacheRoot = Join-Path $projectRoot "Temp\CoSimulationTests\ChamberCache"

if (-not (Test-Path -LiteralPath $hostExe)) { throw "FmuHost was not built: $hostExe" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot "Temp\CoSimulationTests\chamber_individual_smoke.csv"
}
else {
    $OutputPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath))
}
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Send-HostCommand([string]$pipeName, [string]$request, [int]$timeout) {
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

function Read-HostValue([string]$pipeName, [string]$instance, [string]$variableName) {
    $response = Send-HostCommand $pipeName "get instance=$instance name=$variableName" $TimeoutMs
    if ($response -notmatch '(?:^|\s)value=([^\s]+)') { throw "Value missing in response: $response" }
    return [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
}

$rows = [Collections.Generic.List[object]]::new()
foreach ($index in $ChamberIndices) {
    $modelId = "Simple_Chamber_R$index"
    $fmuPath = Join-Path $fmuRoot "$modelId.fmu"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $result = "OK"
    $errorText = ""
    $outputs = @{}
    $process = $null

    try {
        if (-not (Test-Path -LiteralPath $fmuPath)) { throw "FMU was not found: $fmuPath" }
        $hash = (Get-FileHash -LiteralPath $fmuPath -Algorithm SHA256).Hash.Substring(0, 16)
        $cache = Join-Path $cacheRoot "${modelId}_$hash"
        if (-not (Test-Path -LiteralPath $cache)) {
            New-Item -ItemType Directory -Force -Path $cache | Out-Null
            [IO.Compression.ZipFile]::ExtractToDirectory($fmuPath, $cache)
        }

        $pipeName = "chamber_probe_" + [Guid]::NewGuid().ToString("N")
        $hostLog = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) "$modelId.host.log"
        $process = Start-Process -FilePath $hostExe -ArgumentList @(
            "--pipe", $pipeName, "--plugin", $plugin, "--log", $hostLog
        ) -PassThru -WindowStyle Hidden -WorkingDirectory $cache

        foreach ($attempt in 1..30) {
            try { Send-HostCommand $pipeName "ping" 500 | Out-Null; break }
            catch { if ($attempt -eq 30) { throw }; Start-Sleep -Milliseconds 100 }
        }

        $unzip = [Uri]::EscapeDataString($cache)
        Send-HostCommand $pipeName "load instance=$modelId unzip=$unzip logging=0" $TimeoutMs | Out-Null
        Send-HostCommand $pipeName "setup instance=$modelId start=0 stop=0 hasStop=0 tolerance=0.0001 toleranceDefined=1" $TimeoutMs | Out-Null
        Send-HostCommand $pipeName "enter instance=$modelId" $TimeoutMs | Out-Null
        foreach ($entry in ([ordered]@{
            T_air_dis = 24.0
            RH_air_dis = 50.0
            mfr_air_dis = 0.98
            mfr_leakage = 0.01
        }).GetEnumerator()) {
            $value = $entry.Value.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
            Send-HostCommand $pipeName "set instance=$modelId name=$($entry.Key) value=$value" $TimeoutMs | Out-Null
        }
        Send-HostCommand $pipeName "exit instance=$modelId" $TimeoutMs | Out-Null

        foreach ($stepIndex in 0..($StepCount - 1)) {
            Send-HostCommand $pipeName "step instance=$modelId current=$stepIndex step=1" $TimeoutMs | Out-Null
        }
        foreach ($outputName in @("T_air_suc", "mfr_air_suc", "X_air_suc", "RH_air_suc")) {
            $outputs[$outputName] = Read-HostValue $pipeName $modelId $outputName
        }
        Send-HostCommand $pipeName "unload instance=$modelId" $TimeoutMs | Out-Null
        Send-HostCommand $pipeName "shutdown" $TimeoutMs | Out-Null
        if (-not $process.WaitForExit(3000)) { throw "FmuHost did not exit after shutdown." }
    }
    catch {
        $result = if ($_.Exception.Message -match "timed out") { "TIMEOUT" } else { "ERROR" }
        $errorText = $_.Exception.Message
    }
    finally {
        if ($null -ne $process) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill()
                    $process.WaitForExit(3000) | Out-Null
                }
            }
            catch { Write-Warning "Could not stop $modelId host: $($_.Exception.Message)" }
            $process.Dispose()
        }
    }

    $rows.Add([pscustomobject]@{
        Model = $modelId
        Result = $result
        ElapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
        T_air_suc = $outputs.T_air_suc
        mfr_air_suc = $outputs.mfr_air_suc
        X_air_suc = $outputs.X_air_suc
        RH_air_suc = $outputs.RH_air_suc
        Error = $errorText
    })
}

$rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
$rows | Format-Table -AutoSize
Write-Output "RESULT_CSV=$OutputPath"
if ($rows.Result -contains "TIMEOUT" -or $rows.Result -contains "ERROR") { exit 2 }
