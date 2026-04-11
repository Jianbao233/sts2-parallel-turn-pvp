param(
    [string]$GameRoot = 'K:\SteamLibrary\steamapps\common\Slay the Spire 2'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $GameRoot)) {
    throw "Game root not found: $GameRoot"
}

$launchers = @{
    'launch_opengl - local host.bat' = @'
@echo off
if not exist "%~dp0local_fastmp_logs" mkdir "%~dp0local_fastmp_logs"
"%~dp0SlayTheSpire2.exe" --fastmp=host --log-file "%~dp0local_fastmp_logs\host.log" --rendering-driver opengl3 %*
'@
    'launch_opengl - local join 1001.bat' = @'
@echo off
if not exist "%~dp0local_fastmp_logs" mkdir "%~dp0local_fastmp_logs"
"%~dp0SlayTheSpire2.exe" --fastmp=join -clientId=1001 --log-file "%~dp0local_fastmp_logs\join_1001.log" --rendering-driver opengl3 %*
'@
    'launch_opengl - local join 1002.bat' = @'
@echo off
if not exist "%~dp0local_fastmp_logs" mkdir "%~dp0local_fastmp_logs"
"%~dp0SlayTheSpire2.exe" --fastmp=join -clientId=1002 --log-file "%~dp0local_fastmp_logs\join_1002.log" --rendering-driver opengl3 %*
'@
    'launch_opengl - local both.bat' = @'
@echo off
if not exist "%~dp0local_fastmp_logs" mkdir "%~dp0local_fastmp_logs"
start "STS2 Host" "%~dp0SlayTheSpire2.exe" --fastmp=host --log-file "%~dp0local_fastmp_logs\host.log" --rendering-driver opengl3
timeout /t 2 /nobreak >nul
start "STS2 Join 1001" "%~dp0SlayTheSpire2.exe" --fastmp=join -clientId=1001 --log-file "%~dp0local_fastmp_logs\join_1001.log" --rendering-driver opengl3
'@
}

foreach ($entry in $launchers.GetEnumerator()) {
    $path = Join-Path $GameRoot $entry.Key
    Set-Content -Path $path -Value $entry.Value -Encoding ASCII
    Write-Host "Wrote $path"
}

$appid = Join-Path $GameRoot 'steam_appid.txt'
if (-not (Test-Path $appid)) {
    Set-Content -Path $appid -Value '2868840' -Encoding ASCII
    Write-Host "Created $appid"
}

Write-Host 'Local fastmp launchers installed.'
