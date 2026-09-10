@echo off
rem ============================================================
rem 小辰CAD工具箱 一键安装
rem 把自动加载语句写入本机所有版本的 acad.lsp（2021-2026 全支持），
rem 每次运行都会把旧的 XcTools 加载行替换为当前目录的最新路径。
rem ============================================================
setlocal enableextensions
chcp 936 >nul

set "SRC=%~dp0"
if not exist "%SRC%XcTools.lsp" (
  echo [错误] 未找到 XcTools.lsp，请将本文件放在插件目录内再双击运行。
  pause
  exit /b 1
)
if not exist "%SRC%net8\XcTools.dll" if not exist "%SRC%net48\XcTools.dll" if not exist "%SRC%XcTools.dll" (
  echo [错误] 未找到 XcTools.dll，发布包不完整，请重新解压整个文件夹。
  pause
  exit /b 1
)

rem 生成 load 语句（AutoLISP 字符串用正斜杠路径）
set LD=%SRC:\=/%
set LOADLINE=(vl-catch-all-apply (function load) (list "%LD%XcTools.lsp"))

echo 正在注册自动加载（AutoCAD 2021-2026 全版本，旧路径自动替换为最新）...
set /a N=0
set /a V=0
for /d %%V in ("%APPDATA%\Autodesk\AutoCAD *") do call :scanver "%%~fV"
for /d %%V in ("%LOCALAPPDATA%\Autodesk\AutoCAD *") do call :scanver "%%~fV"

reg add "HKCU\Software\XcTools" /v InstallDir /t REG_SZ /d "%SRC:~0,-1%" /f >nul

echo.
if %V%==0 (
  echo [警告] 未找到任何 AutoCAD 版本目录，请先安装 AutoCAD 后重新运行本安装。
) else (
  echo 安装完成：本机共 %V% 个版本目录，本次写入/更新 %N% 处。
  echo 重启任意版本 AutoCAD 即自动加载最新插件，无需任何操作。
)
echo 插件目录: %SRC:~0,-1%
echo.
pause
exit /b 0

:scanver
for /d %%R in ("%~1\R*") do for /d %%K in ("%%~fR\*") do call :patch "%%~fK\Support"
goto :eof

:patch
if not exist "%~1\" goto :eof
set /a V+=1
rem 先删除 acad.lsp 里所有旧的 XcTools 加载行（不管指向哪个目录），
rem 再追加当前目录的最新路径 —— 保证换目录/换包后一定指向最新。
if exist "%~1\acad.lsp" (
  findstr /v /c:"XcTools.lsp" "%~1\acad.lsp" >"%~1\acad.lsp.tmp" 2>nul
  move /y "%~1\acad.lsp.tmp" "%~1\acad.lsp" >nul 2>&1
)
>>"%~1\acad.lsp" echo ;; XcTools autoloader
>>"%~1\acad.lsp" echo %LOADLINE%
set /a N+=1
echo   已更新: %~1
goto :eof
