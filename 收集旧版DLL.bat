@echo off
rem ============================================================
rem 旧版 AutoCAD 引用 DLL 收集工具
rem 在装有 AutoCAD 2021 / 2022 / 2023 / 2024 的电脑上运行本脚本，
rem 自动从注册表定位安装目录并收集编译所需的 4 个 DLL。
rem 收集完成后请把整个 ref_net48 文件夹发回给开发者。
rem ============================================================
setlocal enableextensions
chcp 936 >nul

set "OUT=%~dp0ref_net48"
md "%OUT%" 2>nul

echo 正在从注册表查找本机 AutoCAD 2021-2024 ...
for /f "tokens=2*" %%A in ('reg query "HKLM\SOFTWARE\Autodesk\AutoCAD" /s /v AcadLocation 2^>nul ^| findstr /i "AcadLocation"') do call :get "%%~B"

echo.
echo 收集结果（%OUT%）:
dir /b "%OUT%\*.dll" 2>nul
echo.
echo 请把整个 ref_net48 文件夹打包发回给开发者，用于编译 2021-2024 版插件。
pause
exit /b 0

:get
echo %~1 | findstr /c:"AutoCAD 2021" /c:"AutoCAD 2022" /c:"AutoCAD 2023" /c:"AutoCAD 2024" >nul || goto :eof
echo   检查: %~1
for %%F in (acmgd.dll acdbmgd.dll accoremgd.dll AdWindows.dll) do (
  if not exist "%OUT%\%%F" if exist "%~1\%%F" (
    copy /y "%~1\%%F" "%OUT%\" >nul
    echo     已收集 %%F
  )
)
goto :eof
