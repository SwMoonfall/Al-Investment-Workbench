param([string]$InnoCompiler = '', [string]$Version = '1.0.0')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Use-LocalDotnet.ps1"
$releaseRoot=Split-Path $PSScriptRoot -Parent
Push-Location $releaseRoot
try {
 New-Item -ItemType Directory -Force outputs/phase8 | Out-Null
 dotnet clean AIInvestmentWorkbench.sln --configuration Release *> outputs/phase8/clean.txt
 if($LASTEXITCODE -ne 0){throw 'clean failed'}
 dotnet restore AIInvestmentWorkbench.sln *> outputs/phase8/restore.txt
 if($LASTEXITCODE -ne 0){throw 'restore failed'}
 dotnet build AIInvestmentWorkbench.sln --configuration Release --no-restore *> outputs/phase8/build.txt
 if($LASTEXITCODE -ne 0){throw 'build failed'}
 dotnet test AIInvestmentWorkbench.sln --configuration Release --no-build --logger trx --results-directory outputs/phase8/TestResults *> outputs/phase8/test.txt
 if($LASTEXITCODE -ne 0){throw 'test failed'}
 dotnet publish src/AIInvestmentWorkbench.App --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o outputs/phase8/app *> outputs/phase8/publish.txt
 if($LASTEXITCODE -ne 0){throw 'publish failed'}
 New-Item -ItemType Directory -Force outputs/phase8/app/docs | Out-Null
 Copy-Item docs/*.md outputs/phase8/app/docs
 Copy-Item README.md outputs/phase8/app/README.md
 if(-not $InnoCompiler) { $candidate=Get-ChildItem (Join-Path $releaseRoot 'work/installer-tools/InnoSetup') -Filter 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -First 1; if($candidate){$InnoCompiler=$candidate.FullName} }
 if(-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)){throw 'Specify installed official Inno Setup compiler with -InnoCompiler'}
 & $InnoCompiler ('/DMyAppVersion='+$Version) installer/Workbench.iss *> outputs/phase8/installer-build.txt
 if($LASTEXITCODE -ne 0){throw 'installer build failed'}
 Get-ChildItem outputs/phase8/installer/*.exe | Get-FileHash -Algorithm SHA256 | Format-List | Out-File outputs/phase8/SHA256.txt
} finally { Pop-Location }


