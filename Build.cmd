@echo off
setlocal
cd /d "%~dp0"
set "BACKUP_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%BACKUP_CSC%" set "BACKUP_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%BACKUP_CSC%" /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /win32icon:bitXbackup.ico /resource:bitXbackup.ico,bitXbackup.ico /resource:7z.exe,7z.exe /resource:7z.dll,7z.dll /out:bitXbackup.exe Program.cs
if errorlevel 1 goto fail
powershell.exe -NoProfile -Command "$p=Start-Process -FilePath (Join-Path (Get-Location) 'bitXbackup.exe') -ArgumentList '--test' -Wait -PassThru;if($p.ExitCode -ne 0){exit $p.ExitCode}"
if errorlevel 1 goto fail
start "" "%~dp0bitXbackup.exe"
exit /b 0
:fail
echo Build or core tests failed. Please share the displayed error.
pause
exit /b 1
