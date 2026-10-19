@echo off
cd /d "%~dp0"
if exist "artifacts\MouseOrbit-Pixel\MouseOrbit.exe" (
  start "" "artifacts\MouseOrbit-Pixel\MouseOrbit.exe"
  exit /b
)
if exist "artifacts\MouseOrbit-B-Recent\MouseOrbit.exe" (
  start "" "artifacts\MouseOrbit-B-Recent\MouseOrbit.exe"
  exit /b
)
if exist "artifacts\MouseOrbit-B\MouseOrbit.exe" (
  start "" "artifacts\MouseOrbit-B\MouseOrbit.exe"
  exit /b
)
if exist "artifacts\MouseOrbit\MouseOrbit.exe" (
  start "" "artifacts\MouseOrbit\MouseOrbit.exe"
  exit /b
)
dotnet run --project "src\MouseOrbit\MouseOrbit.csproj" -c Release
