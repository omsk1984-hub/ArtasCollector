#!/bin/bash
# Корректная остановка контейнеров Artas перед выключением WSL-ВМ.
# Вызывается systemd-сервисом wsl-docker-graceful-shutdown.service при halt/reboot/shutdown.
#
# КРИТИЧНО: этот скрипт должен успевать до того, как hyper-v убьёт WSL-ВМ.
# Мы останавливаем PostgreSQL командой `docker stop` (SIGTERM -> pg корректно
# делает shutdown checkpoint и закрывает WAL), а затем даём ему время завершиться.

set -u

LOG=/var/log/artas-graceful-stop.log
log() { echo "$(date -Is) $*" >> "$LOG" 2>/dev/null || true; }

log "=== Artas graceful stop started ==="

# Проверяем, что docker-демон отвечает
if ! docker info >/dev/null 2>&1; then
    log "docker daemon not available, nothing to stop"
    exit 0
fi

# Останавливаем PostgreSQL с достаточным таймаутом (SIGTERM даёт PG сделать checkpoint)
if docker ps --format '{{.Names}}' | grep -q '^artas_postgres$'; then
    log "stopping artas_postgres (graceful, timeout 60s)"
    docker stop -t 60 artas_postgres >> "$LOG" 2>&1 || log "docker stop artas_postgres failed"
    log "artas_postgres stopped, exit ok"
else
    log "artas_postgres is not running"
fi

# Останавливаем остальные контейнеры Artas (если есть)
for c in $(docker ps --format '{{.Names}}' | grep -E '^artas_' || true); do
    if [ "$c" != "artas_postgres" ]; then
        log "stopping $c (timeout 20s)"
        docker stop -t 20 "$c" >> "$LOG" 2>&1 || true
    fi
done

log "=== Artas graceful stop finished ==="
exit 0
