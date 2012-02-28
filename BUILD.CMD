@echo off

setlocal enabledelayedexpansion

set VCVARSALL="%ProgramFiles(x86)%\Microsoft Visual Studio 10.0\VC\vcvarsall.bat"
set DEVENV="devenv.exe"
set RAR="%ProgramFiles%\WinRAR\WinRAR.exe"
set SIGNCMD=signtool.exe sign /n "Next Generation Software" /sha1 "c4ece717747eb7d0cac824f6c7431e5237138b02" /t http://timestamp.verisign.com/scripts/timstamp.dll

rem Windows Sysinternals Sigcheck from http://technet.microsoft.com/en-us/sysinternals/bb897441
set SIGCHECK="%ProgramFiles(x86)%\Sigcheck\sigcheck.exe"

call %VCVARSALL% x86

goto :test

rmdir /s /q "%~dp0\VncSharp\bin" > nul 2>&1
rmdir /s /q "%~dp0\VncSharp\obj" > nul 2>&1

echo Building release version...
%DEVENV% "%~dp0\VncSharp.sln" /build "Release"

echo Signing binaries...
%SIGNCMD% "%~dp0\VncSharp\bin\Release\VncSharpNG.dll"

mkdir "%~dp0\Release" > nul 2>&1

:test

echo Getting product version...
set SIGCHECK=!SIGCHECK:"=^"!
set SIGCHECK=!SIGCHECK: =^^ !
set SIGCHECK=!SIGCHECK:(=^^(!
set SIGCHECK=!SIGCHECK:)=^^)!
for /F "usebackq delims=. tokens=1-4" %%i in (`!SIGCHECK! /accepteula -q -n "%~dp0\VncSharp\bin\Release\VncSharpNG.dll"`) do (
   set PRODUCT_VERSION=%%i.%%j.%%k.%%l
   set PRODUCT_VERSION_SHORT=%%i.%%j
   set PRODUCT_VERSION_MAJOR=%%i
   set PRODUCT_VERSION_MINOR=%%j
)
echo Version is %PRODUCT_VERSION%

set BINARYZIP="%~dp0\Release\VncSharp-%PRODUCT_VERSION%.zip"

echo Creating release ZIP file...
del /f /q %BINARYZIP% > nul 2>&1
%RAR% a -m5 -r -ep1 -afzip -inul %BINARYZIP% "%~dp0\VncSharp\bin\Release\*.*"
%RAR% a -m5 -ep -afzip -inul %BINARYZIP% "%~dp0\*.TXT"

echo.
echo Build process complete.
echo.
pause
