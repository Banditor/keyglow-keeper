@echo off
setlocal

taskkill /IM KeyGlowKeeper.exe /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v KeyGlowKeeper /f >nul 2>&1
reg delete "HKCU\Software\KeyGlowKeeper" /f >nul 2>&1
del /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\KeyGlow Keeper.lnk" >nul 2>&1
rmdir /S /Q "%LOCALAPPDATA%\KeyGlowKeeper" >nul 2>&1

echo.
echo KeyGlow Keeper was removed successfully.
pause
