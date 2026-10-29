@echo off
title Windows Network Setup (PXE Boot)
color 1f
cls
echo ==============================================================================
echo             WINDOWS AUTOMATED NETWORK INSTALLATION (PXE / WinPE)
echo ==============================================================================
echo.

echo [1/3] Initializing Windows PE network subsystem (wpeinit)...
wpeinit

echo [2/3] Waiting for network link and DHCP IP lease...
set RETRY_COUNT=0

:check_network
timeout /t 2 /nobreak >nul
ipconfig | find /i "IPv4" >nul
if %errorlevel% equ 0 goto network_ready

set /a RETRY_COUNT+=1
if %RETRY_COUNT% geq 20 (
    echo.
    echo [ERROR] Timed out waiting for an IP address!
    echo Please verify physical link and DHCP server.
    echo Dropping to shell...
    cmd /k
)
echo Retrying network detection (%RETRY_COUNT%/20)...
goto check_network

:network_ready
echo [+] Network interface is up! Assigned IP:
ipconfig | findstr /i "IPv4"

echo.
echo [3/3] Mounting installation share \\SERVER_IP_PLACEHOLDER\install...
net use Z: \\SERVER_IP_PLACEHOLDER\install /user:guest ""
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Failed to mount SMB share \\SERVER_IP_PLACEHOLDER\install.
    echo Possible causes:
    echo  - Samba service is down on PXE server.
    echo  - Firewall blocking port 445.
    echo  - Hostname/IP unreachable.
    echo Dropping to shell for manual diagnosis...
    cmd /k
)

echo [+] Installation share mounted successfully on drive Z:
echo.
if exist Z:\setup.exe (
    echo ==============================================================================
    echo Starting Windows Setup from Z:\setup.exe...
    echo ==============================================================================
    Z:\setup.exe
) else (
    echo [ERROR] Z:\setup.exe not found on share!
    echo Contents of Z:
    dir Z:\
    cmd /k
)

