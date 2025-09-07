@echo off
pushd %~dp0\..\
call Vendor\bin\premake\premake5.exe vs2022
popd
IF %ERRORLEVEL% NEQ 0 (
  PAUSE
)