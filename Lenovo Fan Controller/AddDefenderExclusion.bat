@echo off
chcp 65001 >nul
setlocal

:: 检查管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 错误：此脚本必须以管理员身份运行。
    echo 请右键单击脚本并选择“以管理员身份运行”。
    pause
    exit /b
)

:: 获取 DLL 相对于此脚本的路径
set "DLL_PATH=%~dp0Fan Control\WinRing0x64.dll"

echo.
echo 正在将以下路径添加到 Windows Defender 排除项：
echo "%DLL_PATH%"
echo.

:: 执行 PowerShell 命令以添加排除项
powershell -Command "Add-MpPreference -ExclusionPath '%DLL_PATH%'"

if %errorLevel% == 0 (
    echo.
    echo 成功：已添加 Windows Defender 永久排除项。
    echo.
) else (
    echo.
    echo 失败：无法添加排除项。
    echo 请确认 Windows Defender 已启用且你拥有相应权限。
    echo.
)

pause
