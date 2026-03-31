@echo off
setlocal

set "URL=chrome://extensions/"
set "CHROME=%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"
if exist "%CHROME%" (
    start "" "%CHROME%" --new-tab "%URL%"
    exit /b 0
)

set "CHROME=%ProgramFiles%\Google\Chrome\Application\chrome.exe"
if exist "%CHROME%" (
    start "" "%CHROME%" --new-tab "%URL%"
    exit /b 0
)

set "CHROME=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"
if exist "%CHROME%" (
    start "" "%CHROME%" --new-tab "%URL%"
    exit /b 0
)

start "" "%URL%"
exit /b 0
