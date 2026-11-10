@echo off
rem Builds the 32-bit gameforge_api.dll replacement with MSVC (Visual Studio 2022 or its Build Tools).
rem Output goes to prebuilt\, which is committed so the launcher can be published on machines without MSVC.
setlocal
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS=%%i"
if not defined VS (echo Visual Studio with C++ tools not found & exit /b 1)
call "%VS%\VC\Auxiliary\Build\vcvars32.bat" >nul 2>nul || exit /b 1
cd /d "%~dp0"
if not exist obj mkdir obj
if not exist prebuilt mkdir prebuilt
cl /nologo /LD /O2 /MT /EHsc /std:c++17 /W4 /WX gameforge_api.cpp /Foobj\ /Feprebuilt\gameforge_api.dll /link /DEF:gameforge_api.def /MACHINE:X86 /IMPLIB:obj\gameforge_api.lib || exit /b 1
del /q prebuilt\*.exp prebuilt\*.lib 2>nul
echo Built %~dp0prebuilt\gameforge_api.dll
