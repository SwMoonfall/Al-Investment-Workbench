$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Use-LocalDotnet.ps1"
$projectRoot = Split-Path $PSScriptRoot -Parent
$aiServicesRoot = Join-Path $projectRoot ('work/ai-services-ui-' + [guid]::NewGuid().ToString('N'))
$appDll = Join-Path $projectRoot 'src/AIInvestmentWorkbench.App/bin/Debug/net10.0-windows/AIInvestmentWorkbench.App.dll'
& dotnet $appDll --ai-services-test --data-root $aiServicesRoot
if ($LASTEXITCODE -ne 0) { throw "AI services validation failed. Inspect $aiServicesRoot/Logs" }
Get-Content (Join-Path $aiServicesRoot 'ai-services-result.json')
Write-Output "Previews: $aiServicesRoot"
