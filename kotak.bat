@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

if "%1"=="" goto help
if "%1"=="run" goto run
if "%1"=="publish" goto publish
if "%1"=="clean" goto clean
if "%1"=="help" goto help
goto help


:publish
echo ========================================
echo Publishing KOTAK (Release)...
echo ========================================
if not exist "publish" mkdir publish

dotnet publish src\Kotak.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish

if %ERRORLEVEL% NEQ 0 (
    echo Publish FAILED!
    exit /b 1
)

echo.
echo ========================================
echo Publish completed!
echo Output: publish\Kotak.exe
echo ========================================
goto end

:run
echo Starting KOTAK...
if not exist "bin\Debug\Kotak.exe" (
    echo Building project first...
    dotnet build src\Kotak.csproj -c Debug
    if %ERRORLEVEL% NEQ 0 exit /b 1
)
start "" "bin\Debug\Kotak.exe"
goto end

:clean
echo Cleaning build outputs...
if exist "bin" rmdir /S /Q bin
if exist "src\bin" rmdir /S /Q src\bin
if exist "src\obj" rmdir /S /Q src\obj
if exist "publish" rmdir /S /Q publish
echo Clean completed!
goto end

:help
echo.
echo KOTAK Build Script
echo ========================================
echo Usage: kotak.bat [command]
echo.
echo Commands:
echo   run       Build and run the application
echo   publish   Create Release single-file executable
echo   clean     Clean all build outputs
echo   help      Show this help message
echo.
echo Examples:
echo   kotak.bat run
echo   kotak.bat publish
echo.
goto end

:end
endlocal
