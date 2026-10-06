$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Use-LocalDotnet.ps1"
$workbenchRoot = Split-Path $PSScriptRoot -Parent
$smokeRoot = Join-Path $workbenchRoot ('work/smoke-' + [guid]::NewGuid().ToString('N'))
$appDll = Join-Path $workbenchRoot 'src/AIInvestmentWorkbench.App/bin/Debug/net10.0-windows/AIInvestmentWorkbench.App.dll'
& dotnet $appDll --smoke-test --data-root $smokeRoot
if ($LASTEXITCODE -ne 0) { throw "WPF smoke check failed. Inspect $smokeRoot/Logs" }
Get-Content (Join-Path $smokeRoot 'smoke-result.json')
