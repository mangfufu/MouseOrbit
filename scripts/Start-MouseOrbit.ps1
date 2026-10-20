#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $SetupOnly,
    [switch] $NoLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$transcribing = $false
$result = 0
$logPath = $null

function Test-DotNetEnvironment([string] $Executable) {
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { return $false }
    try {
        $sdks = @(& $Executable --list-sdks 2>$null)
        if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.0\.\d+ \[')) { return $false }
        $selectedSdk = & $Executable --version 2>$null
        if ($LASTEXITCODE -ne 0 -or $selectedSdk -notmatch '^10\.0\.\d+$') { return $false }
        $sdkProps = Join-Path (Split-Path -Parent $Executable) "sdk\$selectedSdk\Sdks\Microsoft.NET.Sdk\Sdk\Sdk.props"
        if (-not (Test-Path -LiteralPath $sdkProps -PathType Leaf)) { return $false }
        $runtimes = @(& $Executable --list-runtimes 2>$null)
        return ($LASTEXITCODE -eq 0 -and
            [bool]($runtimes -match '^Microsoft.NETCore.App 10\.0\.\d+ \[') -and
            [bool]($runtimes -match '^Microsoft.WindowsDesktop.App 10\.0\.\d+ \['))
    }
    catch { return $false }
}

try {
    if ($env:OS -ne 'Windows_NT') { throw 'MouseOrbit requires Windows.' }
    $stateRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MouseOrbit'
    $logRoot = Join-Path $stateRoot 'logs'
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $logPath = Join-Path $logRoot ("launcher-{0}-{1}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)
    Start-Transcript -Path $logPath | Out-Null
    $transcribing = $true
    Set-Location -LiteralPath $repoRoot

    $osArchitecture = $env:PROCESSOR_ARCHITEW6432
    if (-not $osArchitecture) { $osArchitecture = $env:PROCESSOR_ARCHITECTURE }
    $architecture = switch ($osArchitecture) {
        'AMD64' { 'x64' }
        'ARM64' { 'arm64' }
        default { throw "Unsupported Windows architecture: $osArchitecture" }
    }
    $installDir = Join-Path $stateRoot "dotnet\$architecture"
    $localDotNet = Join-Path $installDir 'dotnet.exe'
    $candidates = @($localDotNet)
    $candidates += @(Get-Command dotnet.exe -CommandType Application -All -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Source)
    $programFilesRoot = $env:ProgramW6432
    if (-not $programFilesRoot) { $programFilesRoot = $env:ProgramFiles }
    if ($programFilesRoot) { $candidates += Join-Path $programFilesRoot 'dotnet\dotnet.exe' }
    $candidates += Join-Path ([Environment]::GetFolderPath('UserProfile')) '.dotnet\dotnet.exe'

    Write-Host '[1/3] Checking .NET 10 SDK and Windows Desktop Runtime...'
    $dotNet = $null
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-DotNetEnvironment $candidate) { $dotNet = $candidate; break }
    }
    if (-not $dotNet) {
        Write-Host "Installing .NET 10 for the current user ($architecture). No administrator rights needed."
        Write-Host "Destination: $installDir"
        Write-Host 'The first download can take several minutes. Please keep this window open.'
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $installer = Join-Path ([IO.Path]::GetTempPath()) ("MouseOrbit-dotnet-install-{0}.ps1" -f [Guid]::NewGuid())
        try {
            # Resolve the stable version from a small official manifest instead of probing archive redirects.
            $versionText = (Invoke-WebRequest -UseBasicParsing -Uri 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0/latest.version' -TimeoutSec 120).Content
            $latestSdk = (-split $versionText)[-1]
            if ($latestSdk -notmatch '^10\.0\.\d+$') { throw 'The official .NET SDK version manifest is invalid.' }
            # An interrupted extraction can leave a version directory that the installer treats as installed.
            $sdkDir = [IO.Path]::GetFullPath((Join-Path $installDir "sdk\$latestSdk"))
            $sdkParent = [IO.Path]::GetFullPath((Join-Path $installDir 'sdk')) + [IO.Path]::DirectorySeparatorChar
            if (-not $sdkDir.StartsWith($sdkParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid SDK repair path.' }
            if ((Test-Path -LiteralPath $sdkDir) -and
                -not (Test-Path -LiteralPath (Join-Path $sdkDir 'Sdks\Microsoft.NET.Sdk\Sdk\Sdk.props'))) {
                Write-Host 'Removing the incomplete private SDK before retrying installation...'
                Remove-Item -LiteralPath $sdkDir -Recurse -Force
            }
            Write-Host "Downloading .NET SDK $latestSdk..."
            Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -TimeoutSec 120
            # Run the official installer in a child process so its exit cannot bypass our error handling.
            & "$PSHOME\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File $installer `
                -Version $latestSdk -Architecture $architecture -InstallDir $installDir -NoPath
            if ($LASTEXITCODE -ne 0) { throw "The .NET SDK installer failed (exit $LASTEXITCODE). Check your network and retry." }
            if (-not (Test-DotNetEnvironment $localDotNet)) {
                throw 'The installation is incomplete: .NET 10 SDK and Windows Desktop Runtime are required. Retry the launcher.'
            }
            $dotNet = $localDotNet
        }
        finally {
            if (Test-Path -LiteralPath $installer) { Remove-Item -LiteralPath $installer -Force }
        }
    }

    # Only configure this process and its children; do not change machine/user PATH or older runtimes.
    $dotNetRoot = Split-Path -Parent $dotNet
    $env:DOTNET_ROOT = $dotNetRoot
    Set-Item -Path "Env:DOTNET_ROOT_$($architecture.ToUpperInvariant())" -Value $dotNetRoot
    $env:PATH = "$dotNetRoot;$env:PATH"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $sdkVersion = & $dotNet --version
    if ($LASTEXITCODE -ne 0) { throw 'The selected SDK cannot build this project.' }
    Write-Host "Using .NET SDK $sdkVersion ($dotNet)"
    if (-not $SetupOnly) {
        Write-Host '[2/3] Restoring dependencies and building MouseOrbit...'
        $project = Join-Path $repoRoot 'src\MouseOrbit\MouseOrbit.csproj'
        & $dotNet build $project --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE). See the build errors above." }
        if (-not $NoLaunch) {
            Write-Host '[3/3] Starting MouseOrbit...'
            $appExe = Join-Path $repoRoot 'src\MouseOrbit\bin\Release\net10.0-windows\MouseOrbit.exe'
            $appLog = Join-Path $logRoot ("app-{0}-{1}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)
            # The GUI app inherits DOTNET_ROOT, so the private runtime works without a system install.
            $app = Start-Process -FilePath $appExe -WorkingDirectory $repoRoot -PassThru `
                -RedirectStandardOutput "$appLog.stdout.log" -RedirectStandardError "$appLog.stderr.log"
            if ($app.WaitForExit(2500) -and $app.ExitCode -ne 0) {
                throw "MouseOrbit exited with code $($app.ExitCode). Application log: $appLog.stderr.log"
            }
            Write-Host 'MouseOrbit started. Use the mouse side button or the system tray icon to open it.'
        }
    }
    Write-Host "Launcher log: $logPath"
}
catch {
    $result = 1
    Write-Host "MouseOrbit failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($logPath) { Write-Host "Launcher log: $logPath" }
}
finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
}
exit $result
