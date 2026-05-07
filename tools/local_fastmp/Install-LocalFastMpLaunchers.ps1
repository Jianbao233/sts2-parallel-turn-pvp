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
"%~dp0SlayTheSpire2.exe" --fastmp=host --parallelturnpvphost --log-file "%~dp0local_fastmp_logs\host.log" --rendering-driver opengl3 %*
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
setlocal
if not exist "%~dp0local_fastmp_logs" mkdir "%~dp0local_fastmp_logs"
set "HOST_LOG=%~dp0local_fastmp_logs\host.log"
set "JOIN_LOG=%~dp0local_fastmp_logs\join_1001.log"
if exist "%HOST_LOG%" del "%HOST_LOG%"
if exist "%JOIN_LOG%" del "%JOIN_LOG%"
start "STS2 Host" "%~dp0SlayTheSpire2.exe" --fastmp=host --parallelturnpvphost --log-file "%HOST_LOG%" --rendering-driver opengl3
set /a WAIT_SECONDS=0
:wait_for_host
if exist "%HOST_LOG%" (
    findstr /C:"DirectHost started on port 33771" "%HOST_LOG%" >nul 2>&1
    if not errorlevel 1 goto start_join
)
if %WAIT_SECONDS% GEQ 60 goto host_timeout
timeout /t 1 /nobreak >nul
set /a WAIT_SECONDS+=1
goto wait_for_host

:host_timeout
echo Host did not create a room within 60 seconds. Check "%HOST_LOG%".
pause
exit /b 1

:start_join
start "STS2 Join 1001" "%~dp0SlayTheSpire2.exe" --fastmp=join -clientId=1001 --log-file "%~dp0local_fastmp_logs\join_1001.log" --rendering-driver opengl3
endlocal
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
