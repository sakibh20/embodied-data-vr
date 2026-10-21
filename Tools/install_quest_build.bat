@echo off
REM Installs Builds\Quest\EmbodiedDataViz.apk on the connected Quest (keeps existing study data).
REM Headset on, "Allow USB debugging" accepted. A phone plugged in at the same time is ignored.
setlocal
set ADB="C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
%ADB% start-server >nul 2>&1
set QUEST=
REM (device list goes through a temp file: cmd's for /f can't run a quoted path with spaces plus a pipe)
%ADB% devices -l > "%TEMP%\quest_devices.txt" 2>&1
for /f "tokens=1,2" %%a in ('findstr /c:"model:Quest" "%TEMP%\quest_devices.txt"') do if "%%b"=="device" set QUEST=%%a
if "%QUEST%"=="" (
  echo No Quest found. Connected devices:
  %ADB% devices -l
  echo Check: headset on, USB debugging allowed in the headset, Developer Mode on.
  pause & exit /b 1
)
echo Using Quest %QUEST%
REM -r = replace the installed app but KEEP its data (never uninstall: that deletes StudyData)
%ADB% -s %QUEST% install -r "%~dp0..\Builds\Quest\EmbodiedDataViz.apk"
if errorlevel 1 (echo. & echo FAILED.) else (echo. & echo Installed. In the headset: Library ^> Unknown Sources ^> Thesis)
pause
