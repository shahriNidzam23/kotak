@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

if "%1"=="" goto help
if "%1"=="run" goto run
if "%1"=="publish" goto publish
if "%1"=="release" goto release
if "%1"=="clean" goto clean
if "%1"=="version" goto showversion
if "%1"=="help" goto help
goto help


:publish
call :do_publish
goto end

:do_publish
echo ========================================
echo Publishing KOTAK (Release)
echo ========================================

REM Read current version from csproj using PowerShell for reliability
set "CSPROJ=src\Kotak.csproj"
set "CURRENT_VERSION="
for /f "delims=" %%a in ('powershell -Command "([xml](Get-Content '%CSPROJ%')).Project.PropertyGroup.Version | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | Select-Object -First 1"') do set "CURRENT_VERSION=%%a"

if "!CURRENT_VERSION!"=="" set "CURRENT_VERSION=1.0.0"

REM Parse version components
for /f "tokens=1,2,3 delims=." %%a in ("!CURRENT_VERSION!") do (
    set "MAJOR=%%a"
    set "MINOR=%%b"
    set "PATCH=%%c"
)

echo.
echo Current version: %CURRENT_VERSION%
echo.
echo Select version update type:
echo   [1] Major (breaking changes)     - %MAJOR%.%MINOR%.%PATCH% -^> !MAJOR! + 1.0.0
set /a "NEW_MAJOR=MAJOR+1"
echo       Example: %CURRENT_VERSION% -^> !NEW_MAJOR!.0.0
echo   [2] Minor (new features)         - %MAJOR%.%MINOR%.%PATCH% -^> %MAJOR%.!MINOR! + 1.0
set /a "NEW_MINOR=MINOR+1"
echo       Example: %CURRENT_VERSION% -^> %MAJOR%.!NEW_MINOR!.0
echo   [3] Patch (bug fixes)            - %MAJOR%.%MINOR%.%PATCH% -^> %MAJOR%.%MINOR%.!PATCH! + 1
set /a "NEW_PATCH=PATCH+1"
echo       Example: %CURRENT_VERSION% -^> %MAJOR%.%MINOR%.!NEW_PATCH!
echo   [4] Keep current version (%CURRENT_VERSION%)
echo   [5] Cancel
echo.

choice /c 12345 /n /m "Enter choice [1-5]: "
set "CHOICE=%ERRORLEVEL%"

if %CHOICE%==5 (
    echo Publish cancelled.
    exit /b 1
)

if %CHOICE%==1 (
    set /a "MAJOR=MAJOR+1"
    set "MINOR=0"
    set "PATCH=0"
)
if %CHOICE%==2 (
    set /a "MINOR=MINOR+1"
    set "PATCH=0"
)
if %CHOICE%==3 (
    set /a "PATCH=PATCH+1"
)

set "NEW_VERSION=!MAJOR!.!MINOR!.!PATCH!"

if %CHOICE% NEQ 4 (
    echo.
    echo Updating version: %CURRENT_VERSION% -^> !NEW_VERSION!

    REM Update version in csproj using PowerShell for reliability
    powershell -Command "(Get-Content '%CSPROJ%') -replace '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>', '<Version>!NEW_VERSION!</Version>' -replace '<FileVersion>[0-9]+\.[0-9]+\.[0-9]+</FileVersion>', '<FileVersion>!NEW_VERSION!</FileVersion>' -replace '<AssemblyVersion>[0-9]+\.[0-9]+\.[0-9]+</AssemblyVersion>', '<AssemblyVersion>!NEW_VERSION!</AssemblyVersion>' | Set-Content '%CSPROJ%'"

    if !ERRORLEVEL! NEQ 0 (
        echo Failed to update version!
        exit /b 1
    )
) else (
    set "NEW_VERSION=%CURRENT_VERSION%"
)

echo.
echo ========================================
echo Building KOTAK v!NEW_VERSION!...
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
echo Version: !NEW_VERSION!
echo Output: publish\Kotak.exe
echo ========================================
exit /b 0

:release
echo ========================================
echo KOTAK Release to GitHub
echo ========================================

REM Check if gh CLI is available
where gh >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: GitHub CLI [gh] is not installed.
    echo Install from: https://cli.github.com/
    exit /b 1
)

REM Check if authenticated
gh auth status >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Not authenticated with GitHub CLI.
    echo Run: gh auth login
    exit /b 1
)

REM Run publish first (includes version prompt and build)
call :do_publish
if !ERRORLEVEL! NEQ 0 exit /b 1

REM Get the version that was just set
for /f "delims=" %%a in ('powershell -Command "([xml](Get-Content 'src\Kotak.csproj')).Project.PropertyGroup.Version | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | Select-Object -First 1"') do set "RELEASE_VER=%%a"

echo.
echo ========================================
echo Creating GitHub Release v!RELEASE_VER!
echo ========================================

REM Git operations
git add .
git commit -m "Release v!RELEASE_VER!" 2>nul
git tag -a "v!RELEASE_VER!" -m "Release v!RELEASE_VER!" 2>nul

echo Pushing to GitHub...
git push
git push --tags

REM Create GitHub release with exe
echo Creating release on GitHub...
gh release create "v!RELEASE_VER!" "publish\Kotak.exe" --title "KOTAK v!RELEASE_VER!" --notes "Release v!RELEASE_VER!" --latest

if !ERRORLEVEL! EQU 0 (
    echo.
    echo ========================================
    echo Release v!RELEASE_VER! created!
    echo https://github.com/shahriNidzam23/kotak/releases/tag/v!RELEASE_VER!
    echo ========================================
) else (
    echo Release creation failed!
    exit /b 1
)
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

:showversion
set "CSPROJ=src\Kotak.csproj"
for /f "delims=" %%a in ('powershell -Command "([xml](Get-Content '%CSPROJ%')).Project.PropertyGroup.Version | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | Select-Object -First 1"') do set "SHOW_VERSION=%%a"
echo KOTAK version: !SHOW_VERSION!
goto end

:help
echo.
echo KOTAK Build Script
echo ========================================
echo Usage: kotak.bat [command]
echo.
echo Commands:
echo   run       Build and run the application
echo   publish   Create Release executable (prompts for version)
echo   release   Publish and create GitHub release (requires gh CLI)
echo   version   Show current version
echo   clean     Clean all build outputs
echo   help      Show this help message
echo.
echo Examples:
echo   kotak.bat run
echo   kotak.bat publish
echo   kotak.bat release
echo   kotak.bat version
echo.
goto end

:end
endlocal
