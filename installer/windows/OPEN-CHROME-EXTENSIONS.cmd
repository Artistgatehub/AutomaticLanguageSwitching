@echo off
setlocal

set "URL=chrome://extensions/"
set "CHROME="

for /f "delims=" %%I in ('where chrome 2^>nul') do if not defined CHROME if exist "%%~fI" set "CHROME=%%~fI"
if defined CHROME goto launch

call :read_app_path "HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
if defined CHROME goto launch

call :read_app_path "HKLM\Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
if defined CHROME goto launch

set "CHROME=%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"
if exist "%CHROME%" goto launch

set "CHROME=%ProgramFiles%\Google\Chrome\Application\chrome.exe"
if exist "%CHROME%" goto launch

set "CHROME=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"
if exist "%CHROME%" goto launch

exit /b 0

:launch
start "" "%CHROME%" "%URL%" >nul 2>&1
exit /b 0

:read_app_path
for /f "skip=2 tokens=1,2,*" %%A in ('reg query "%~1" /ve 2^>nul') do (
    if /i "%%B"=="REG_SZ" if not defined CHROME if exist "%%C" set "CHROME=%%C"
)
exit /b 0
