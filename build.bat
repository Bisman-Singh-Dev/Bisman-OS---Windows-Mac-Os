@echo off
rem Builds BismanOS.exe (Bisman OS 2) from src\*.cs.
rem Uses the modern C# compiler from the .NET SDK, targeting the .NET Framework that ships with Windows.
cd /d "%~dp0"
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set CSC=
for /d %%d in ("C:\Program Files\dotnet\sdk\*") do if exist "%%d\Roslyn\bincore\csc.dll" set CSC=%%d\Roslyn\bincore\csc.dll
if "%CSC%"=="" (echo The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download & exit /b 1)
dotnet "%CSC%" /nologo /noconfig /nostdlib /langversion:latest /nullable:disable /optimize+ /target:winexe /platform:x64 /utf8output /out:BismanOS.exe ^
  /r:"%FW%\mscorlib.dll" /r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.Xml.dll" ^
  /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" ^
  /r:"%FW%\Microsoft.CSharp.dll" /r:"%FW%\Microsoft.VisualBasic.dll" /r:"%FW%\System.Management.dll" /r:"%FW%\System.Data.dll" /r:"%FW%\System.Windows.Forms.dll" ^
  src\*.cs
if %errorlevel%==0 (echo Built BismanOS.exe) else (echo Build failed & exit /b 1)
