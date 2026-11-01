@echo off
rem Builds 7-Zip's 7z.dll and sevenzipkit.dll for one Windows architecture and
rem copies them into artifacts\native\win-<arch>.
rem
rem   scripts\build-win.cmd x64|arm64 [7-Zip source] [bit7z source]
rem
rem The sources default to the trees scripts/fetch-sources.sh unpacks into _deps.
rem Run from any prompt; it sets up the Visual Studio build environment itself.
setlocal
set ARCH=%~1
set ROOT=%~dp0..
if "%ARCH%"=="" goto usage
if not "%ARCH%"=="x64" if not "%ARCH%"=="arm64" goto usage
if "%~2"=="" (set SRC7Z=%ROOT%\_deps\7zip-src) else (set SRC7Z=%~f2)
if "%~3"=="" (set SRCBIT7Z=%ROOT%\_deps\bit7z-src) else (set SRCBIT7Z=%~f3)
if not exist "%SRC7Z%\CPP\7zip" (echo 7-Zip source not found at %SRC7Z%; run scripts/fetch-sources.sh & exit /b 1)
if not exist "%SRCBIT7Z%\CMakeLists.txt" (echo bit7z source not found at %SRCBIT7Z%; run scripts/fetch-sources.sh & exit /b 1)

set OUT=%ROOT%\artifacts\native\win-%ARCH%
set BUILD=%ROOT%\build\win-%ARCH%

if "%ARCH%"=="x64" (set VCARCH=x64) else (set VCARCH=x64_arm64)
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath`) do set VSDIR=%%i
if not exist "%VSDIR%\VC\Auxiliary\Build\vcvarsall.bat" (echo Visual Studio with C++ tools not found & exit /b 1)
call "%VSDIR%\VC\Auxiliary\Build\vcvarsall.bat" %VCARCH% >nul || exit /b 1

rem --- 7z.dll: 7-Zip's full format library (Bundles\Format7zF), built with its own makefiles
pushd "%SRC7Z%\CPP\7zip\Bundles\Format7zF" || exit /b 1
nmake /nologo PLATFORM=%ARCH% || (popd & exit /b 1)
popd

rem --- sevenzipkit.dll: the C interface over bit7z
cmake -S "%ROOT%\native" -B "%BUILD%" -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release ^
  "-DSZK_BIT7Z_DIR=%SRCBIT7Z%" "-DSZK_7ZIP_DIR=%SRC7Z%" || exit /b 1
cmake --build "%BUILD%" || exit /b 1

if not exist "%OUT%" mkdir "%OUT%"
copy /y "%SRC7Z%\CPP\7zip\Bundles\Format7zF\%ARCH%\7z.dll" "%OUT%\" >nul || exit /b 1
copy /y "%BUILD%\sevenzipkit.dll" "%OUT%\" >nul || exit /b 1
echo Built %OUT%\7z.dll and %OUT%\sevenzipkit.dll
exit /b 0

:usage
echo usage: build-win.cmd x64^|arm64 [7-Zip source] [bit7z source]
exit /b 2
