@echo off
REM ============================================
REM Управление стеком мониторинга Artas2026
REM Запуск из Windows (вызывает WSL bash скрипт)
REM ============================================

set WSL_SCRIPT="/mnt/d/work/Artas2026/scripts/manage_monitoring.sh"

echo [MONITORING] Calling WSL script: %~1
wsl bash -c "cd /mnt/d/work/Artas2026 && chmod +x scripts/manage_monitoring.sh && ./scripts/manage_monitoring.sh %~1 %~2"

exit /b %errorlevel%
