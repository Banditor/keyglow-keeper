@echo off
setlocal
set "APPDIR=%LOCALAPPDATA%\KeyGlowKeeper"
set "APP=%APPDIR%\KeyGlowKeeper.exe"
set "SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\KeyGlow Keeper.lnk"

if not exist "%~dp0KeyGlowKeeper.exe" (
  echo KeyGlowKeeper.exe was not found next to this installer.
  pause
  exit /b 1
)

taskkill /IM KeyGlowKeeper.exe /F >nul 2>&1
if not exist "%APPDIR%" mkdir "%APPDIR%"
copy /Y "%~dp0KeyGlowKeeper.exe" "%APP%" >nul

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut($env:APPDATA+'\Microsoft\Windows\Start Menu\Programs\KeyGlow Keeper.lnk'); $s.TargetPath=$env:LOCALAPPDATA+'\KeyGlowKeeper\KeyGlowKeeper.exe'; $s.WorkingDirectory=$env:LOCALAPPDATA+'\KeyGlowKeeper'; $s.Description='Keep the Lenovo keyboard backlight on'; $s.Save()"

start "" "%APP%"
echo.
echo KeyGlow Keeper was installed and started successfully.
echo The icon may be under the hidden-icons arrow in the notification area.
pause
