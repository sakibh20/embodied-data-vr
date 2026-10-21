@echo off
REM Copies the study data off the Quest to QuestData\<date-time> (git-ignored).
REM Headset on, "Allow USB debugging" accepted. A phone plugged in at the same time is ignored.
setlocal
set ADB="C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
%ADB% start-server >nul 2>&1
set QUEST=
for /f "tokens=1,2" %%a in ('%ADB% devices -l ^| findstr /c:"model:Quest"') do if "%%b"=="device" set QUEST=%%a
if "%QUEST%"=="" (
  echo No Quest found. Connected devices:
  %ADB% devices -l
  echo Check: headset on, USB debugging allowed in the headset, Developer Mode on.
  pause & exit /b 1
)
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set STAMP=%%i
set DEST=%~dp0..\QuestData\%STAMP%
mkdir "%DEST%"
%ADB% -s %QUEST% pull /sdcard/Android/data/com.sakibhasan.embodieddataviz/files/StudyData "%DEST%"
if errorlevel 1 (echo. & echo FAILED to copy. Is the app installed and has a session been run?) else (echo. & echo Copied to %DEST%)
pause
