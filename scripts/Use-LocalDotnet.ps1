# Dot-source this file: . ./scripts/Use-LocalDotnet.ps1
$workbenchRoot = Split-Path $PSScriptRoot -Parent
$workbenchSdk = Join-Path $workbenchRoot 'work/dotnet'
if (Test-Path (Join-Path $workbenchSdk 'dotnet.exe')) {
    $env:DOTNET_ROOT = $workbenchSdk
    $env:PATH = "$workbenchSdk;$env:PATH"
    $env:DOTNET_CLI_HOME = Join-Path $workbenchRoot 'work/cli'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
dotnet --version
