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

set TARGET_DIR=Z:\TARGET_SLUG_PLACEHOLDER\iso_extracted
if not exist "%TARGET_DIR%\setup.exe" set TARGET_DIR=Z:\TARGET_SLUG_PLACEHOLDER
if not exist "%TARGET_DIR%\setup.exe" set TARGET_DIR=Z:\iso_extracted
if not exist "%TARGET_DIR%\setup.exe" set TARGET_DIR=Z:\

echo [+] Using installation media from: %TARGET_DIR%
cd /d "%TARGET_DIR%"

set INSTALL_OPT=
if exist "%TARGET_DIR%\sources\install.esd" set INSTALL_OPT=/installfrom:"%TARGET_DIR%\sources\install.esd"
if exist "%TARGET_DIR%\sources\install.wim" set INSTALL_OPT=/installfrom:"%TARGET_DIR%\sources\install.wim"

set UNATTEND_OPT=
if exist "%TARGET_DIR%\autounattend.xml" set UNATTEND_OPT=/unattend:"%TARGET_DIR%\autounattend.xml"
if exist Z:\TARGET_SLUG_PLACEHOLDER\autounattend.xml set UNATTEND_OPT=/unattend:Z:\TARGET_SLUG_PLACEHOLDER\autounattend.xml
if exist X:\autounattend.xml set UNATTEND_OPT=/unattend:X:\autounattend.xml

if exist "%TARGET_DIR%\setup.exe" (
    echo ==============================================================================
    echo Starting Windows Setup (%TARGET_DIR%)...
    echo ==============================================================================
    "%TARGET_DIR%\setup.exe" %INSTALL_OPT% %UNATTEND_OPT%
) else (
    echo [ERROR] setup.exe not found on share!
    echo Contents of %TARGET_DIR%:
    dir "%TARGET_DIR%"
    cmd /k
)

