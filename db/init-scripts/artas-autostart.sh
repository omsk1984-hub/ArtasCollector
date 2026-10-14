#!/bin/bash
# Автозапуск контейнеров Artas при старте WSL-ВМ.
#
# Почему это нужно: при выключении WSL graceful-сервис делает `docker stop`,
# поэтому Docker считает остановку «ручной» и НЕ поднимает контейнеры обратно
# (restart: unless-stopped не помогает). Этот скрипт поднимает их при загрузке ВМ.

set -u

LOG=/var/log/artas-autostart.log
log() { echo "$(date -Is) $*" >> "$LOG" 2>/dev/null || true; }

log "=== Artas autostart started ==="

# Ждём готовности docker-демона
for i in $(seq 1 30); do
    if docker info >/dev/null 2>&1; then
        break
    fi
    log "waiting for docker daemon ($i/30)..."
    sleep 2
done

if ! docker info >/dev/null 2>&1; then
    log "docker daemon not available after wait, aborting"
    exit 1
fi

COMPOSE_DIR=/mnt/d/work/Artas2026/db
if [ ! -d "$COMPOSE_DIR" ]; then
    log "compose dir $COMPOSE_DIR не найден, aborting"
    exit 1
fi

cd "$COMPOSE_DIR" || { log "cd failed"; exit 1; }

log "starting artas_postgres"
docker compose up -d postgres >> "$LOG" 2>&1 || log "failed to start postgres"

# Мониторинг (Loki + Promtail + Prometheus + OTel) — отдельный compose-файл.
MONITORING_DIR=/mnt/d/work/Artas2026/monitoring
if [ -d "$MONITORING_DIR" ] && [ -f "$MONITORING_DIR/docker-compose.monitoring.yml" ]; then
    cd "$MONITORING_DIR" || { log "cd monitoring failed"; exit 1; }
    log "starting monitoring stack (loki/promtail/prometheus/otel)"
    docker compose -f docker-compose.monitoring.yml up -d >> "$LOG" 2>&1 || log "failed to start monitoring"
else
    log "monitoring dir $MONITORING_DIR не найден, пропускаю"
fi

log "=== Artas autostart finished ==="
exit 0
