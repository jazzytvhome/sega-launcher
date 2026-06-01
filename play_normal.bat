@echo off
REM Play the UNMODIFIED dump on http://localhost:8001/
REM Close the minimised server window to stop.

setlocal
set "ROOT=%~dp0normal"
set "PORT=8001"

if not exist "%ROOT%\index.html" (
    echo No index.html at %ROOT%
    echo Re-run dump_site.py and re-copy into normal\ first.
    pause
    exit /b 1
)

start "slowroads NORMAL (close to stop)" /min cmd /c "cd /d ""%ROOT%"" && python -m http.server %PORT%"
timeout /t 2 /nobreak >nul
start "" "http://localhost:%PORT%/"
endlocal
exit /b 0
