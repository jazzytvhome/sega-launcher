@echo off
REM Play the MODDED build on http://localhost:8002/
REM Close the minimised server window to stop.

setlocal
set "ROOT=%~dp0modded"
set "PORT=8002"

if not exist "%ROOT%\index.html" (
    echo No index.html at %ROOT%
    echo Re-run dump_site.py and re-copy into modded\ first.
    pause
    exit /b 1
)

start "slowroads MODDED (close to stop)" /min cmd /c "cd /d ""%ROOT%"" && python -m http.server %PORT%"
timeout /t 2 /nobreak >nul
start "" "http://localhost:%PORT%/"
endlocal
exit /b 0
