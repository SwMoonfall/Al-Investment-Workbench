param([string]$InnoCompiler = '')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
 $uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9758D52B-221C-49CC-8896-1443731F9C69}_is1'
 if(Test-Path $uninstallKey){throw 'An existing installation is registered; validation must not replace a user installation.'}
 if(-not $InnoCompiler){$InnoCompiler=Join-Path $repo 'work/installer-tools/InnoSetup/ISCC.exe'}
 $validation=Join-Path $repo ('work/installer-validation-'+[guid]::NewGuid().ToString('N'))
 $installDir=Join-Path $validation 'Program'
 $dataRoot=Join-Path $validation 'UserData'
 New-Item -ItemType Directory -Force (Join-Path $dataRoot 'Data') | Out-Null
 Copy-Item work/phase7-upgrade/Data/investment.db (Join-Path $dataRoot 'Data/investment.db')
 $dataHash=(Get-FileHash (Join-Path $dataRoot 'Data/investment.db')).Hash
 New-Item -ItemType Directory -Force (Join-Path $dataRoot 'Backups') | Out-Null
 $retainedBackup=Join-Path $dataRoot 'Backups/user-preservation.db'
 Copy-Item (Join-Path $dataRoot 'Data/investment.db') $retainedBackup
 $oldSource=Join-Path $repo 'outputs/phase7/app'
 & $InnoCompiler '/DMyAppVersion=0.9.9' ('/DSourceDir='+$oldSource) '/DOutputBase=UpgradeFixture-0.9.9' installer/Workbench.iss *> outputs/phase8/installer-fixture-build.txt
 if($LASTEXITCODE -ne 0){throw 'Upgrade fixture build failed'}
 $oldSetup=Join-Path $repo 'outputs/phase8/installer/UpgradeFixture-0.9.9.exe'
 function Install-Package([string]$file,[string]$log) {
  $proc=Start-Process -FilePath $file -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS',('/DIR="'+$installDir+'"'),'/GROUP="AI Investment Workbench Validation"',('/LOG="'+$log+'"')) -WindowStyle Hidden -PassThru
  $proc.WaitForExit(); if($proc.ExitCode -ne 0){throw "Install failed: $($proc.ExitCode)"}
 }
 Install-Package $oldSetup (Join-Path $repo 'outputs/phase8/install-old.log')
 $oldVersion=(Get-ItemProperty $uninstallKey).DisplayVersion
 if($oldVersion -ne '0.9.9'){throw 'Old package version not registered'}
 $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Investment Workbench/AI Investment Workbench.lnk'
 if(-not (Test-Path $shortcut)){throw 'Start menu shortcut missing'}
 Install-Package (Join-Path $repo 'outputs/phase8/installer/AIInvestmentWorkbench-1.0.0-win-x64-Setup.exe') (Join-Path $repo 'outputs/phase8/install-upgrade.log')
 $newVersion=(Get-ItemProperty $uninstallKey).DisplayVersion
 if($newVersion -ne '1.0.0'){throw 'Upgrade version not registered'}
 if((Get-FileHash (Join-Path $dataRoot 'Data/investment.db')).Hash -ne $dataHash){throw 'Installer touched user database'}
 $exe=Join-Path $installDir 'AIInvestmentWorkbench.App.exe'
 $proc=Start-Process -FilePath $exe -ArgumentList @('--data-root',('"'+$dataRoot+'"')) -WindowStyle Hidden -PassThru
 $opened=$false
 for($i=0;$i -lt 60;$i++){Start-Sleep -Milliseconds 250;$proc.Refresh();if($proc.HasExited){break};if($proc.MainWindowTitle -like '*AI Investment Workbench*'){$opened=$true;break}}
 if(-not $opened){throw "Installed program failed to open: $($proc.Id)"}
 $title=$proc.MainWindowTitle
 $proc.CloseMainWindow() | Out-Null
 if(-not $proc.WaitForExit(5000) -or $proc.ExitCode -ne 0){throw 'Installed app failed to close'}
 $afterStartHash=(Get-FileHash (Join-Path $dataRoot 'Data/investment.db')).Hash
 $backups=@(Get-ChildItem (Join-Path $dataRoot 'Backups') -Filter '*.db').Count
 $uninstall=Join-Path $installDir 'unins000.exe'
 $proc=Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $repo 'outputs/phase8/uninstall.log')+'"')) -WindowStyle Hidden -PassThru
 $proc.WaitForExit();if($proc.ExitCode -ne 0){throw 'Uninstall failed'}
 for($i=0;$i -lt 20 -and (Test-Path $exe);$i++){Start-Sleep -Milliseconds 250}
 $preserved=(Test-Path (Join-Path $dataRoot 'Data/investment.db')) -and (Get-FileHash (Join-Path $dataRoot 'Data/investment.db')).Hash -eq $afterStartHash
 if(-not $preserved -or -not (Test-Path $retainedBackup) -or (Get-FileHash $retainedBackup).Hash -ne $dataHash -or (Test-Path $exe) -or (Test-Path $shortcut)){throw 'Uninstall preservation or cleanup failed'}
 @{Passed=$true;OldVersion=$oldVersion;NewVersion=$newVersion;StartMenuCreated=$true;InstalledAppOpened=$opened;Title=$title;UserDataPreserved=$preserved;BackupsRetained=$backups;ProgramRemoved=$true;ShortcutRemoved=$true;DataRoot=$dataRoot;CheckedAt=[DateTimeOffset]::Now.ToString('O')} | ConvertTo-Json | Set-Content outputs/phase8/installer-validation.json
 Move-Item -LiteralPath $oldSetup -Destination (Join-Path $validation 'UpgradeFixture-0.9.9.exe')
 Get-Content outputs/phase8/installer-validation.json
} finally {Pop-Location}
