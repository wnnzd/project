\
@echo off
setlocal
set OUTDIR=dist
if not exist %OUTDIR% mkdir %OUTDIR%
dotnet publish FacebookChecker.csproj -c Release -r win-x64 ^
 /p:PublishSingleFile=true ^
 /p:IncludeNativeLibrariesForSelfExtract=true ^
 /p:PublishTrimmed=false ^
 --self-contained true ^
 -o %OUTDIR%
echo.
echo Build done. Output in %OUTDIR%.
pause
