#!/bin/bash
# ============================================
# Управление стеком мониторинга Artas2026 (Loki + Promtail + Prometheus + OTel)
# Для запуска в WSL
# ============================================
# Использование:
#   ./scripts/manage_monitoring.sh start
#   ./scripts/manage_monitoring.sh status
#   ./scripts/manage_monitoring.sh logs [service]
#   ./scripts/manage_monitoring.sh stop
#   ./scripts/manage_monitoring.sh restart
#   ./scripts/manage_monitoring.sh cleanup
# ============================================

set -e

# Определяем пути (relativa от корня проекта)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_DIR="$PROJECT_ROOT/monitoring"
COMPOSE_FILE="docker-compose.monitoring.yml"

# Цвета для вывода
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Функции вывода
info() { echo -e "${CYAN}[MONITORING] $1${NC}"; }
ok()   { echo -e "${GREEN}[OK] $1${NC}"; }
warn() { echo -e "${YELLOW}[WARN] $1${NC}"; }
err()  { echo -e "${RED}[ERROR] $1${NC}"; }

# Проверка Docker
check_docker() {
    if ! command -v docker &> /dev/null; then
        err "Docker не установлен или не запущен в WSL"
        exit 1
    fi
}

# Проверка готовности сервисов
check_services_ready() {
    local max_attempts=10
    local attempt=0
    
    info "Ожидаем запуска сервисов..."
    
    while [ $attempt -lt $max_attempts ]; do
        attempt=$((attempt + 1))
        sleep 3
        
        local loki_ok=false
        
        if curl -s --connect-timeout 2 http://localhost:3100/-/healthy &> /dev/null; then
            loki_ok=true
        fi
        
        if $loki_ok; then
            ok "Все сервисы запущены!"
            return 0
        fi
        
        info "Ожидание... ($attempt/$max_attempts)"
    done
    
    warn "Некоторые сервисы могут быть ещё не готовы"
    return 1
}

# Показать статус
show_status() {
    info "Статус стека мониторинга:"
    cd "$COMPOSE_DIR"
    docker-compose $COMPOSE_FILE ps
    
    echo ""
    info "Проверка здоровья сервисов:"
    
    if curl -s --connect-timeout 2 http://localhost:3100/-/healthy 2>/dev/null; then
        ok "Loki: доступен"
    else
        err "Loki: недоступен"
    fi
}

# Запуск
start_services() {
    check_docker
    
    info "Запуск стека мониторинга..."
    cd "$COMPOSE_DIR"
    
    # --remove-orphans: удаляем контейнеры, осиротевшие после ручных запусков
    # (docker run grafana/promtail ...) или изменения состава сервисов.
    # Без этого флага они продолжают жить вне compose-проекта.
    docker-compose $COMPOSE_FILE up -d --remove-orphans
    
    if [ $? -eq 0 ]; then
        ok "Стек мониторинга запущен"
        sleep 5
        check_services_ready
    else
        err "Не удалось запустить стек мониторинга"
        exit 1
    fi
}

# Остановка
stop_services() {
    info "Остановка стека мониторинга..."
    cd "$COMPOSE_DIR"
    
    docker-compose $COMPOSE_FILE down
    
    if [ $? -eq 0 ]; then
        ok "Стек мониторинга остановлен"
    else
        err "Не удалось остановить стек мониторинга"
        exit 1
    fi
}

# Перезапуск
restart_services() {
    info "Перезапуск стека мониторинга..."
    cd "$COMPOSE_DIR"
    
    docker compose -f "$COMPOSE_FILE" restart
    
    if [ $? -eq 0 ]; then
        ok "Стек мониторинга перезапущен"
    else
        err "Не удалось перезапустить стек мониторинга"
        exit 1
    fi
}

# Логи
show_logs() {
    local service="${1:-}"
    cd "$COMPOSE_DIR"
    
    if [ -n "$service" ]; then
        info "Логи сервиса: $service"
        docker compose -f "$COMPOSE_FILE" logs -f --tail=100 "$service"
    else
        info "Логи всех сервисов (Ctrl+C для выхода)"
        docker-compose $COMPOSE_FILE logs -f --tail=50
    fi
}

# Очистка
cleanup_all() {
    warn "Это удалит ВСЕ данные мониторинга включая логи и конфигурации dashboards!"
    read -p "Введите 'YES' для подтверждения: " confirm
    
    if [ "$confirm" = "YES" ]; then
        cd "$COMPOSE_DIR"
        docker compose -f "$COMPOSE_FILE" down -v
        ok "Все данные мониторинга удалены"
    else
        info "Отмена очистки"
    fi
}

# Помощь
show_help() {
    echo "Использование: $0 {start|stop|restart|status|logs [service]|cleanup|help}"
    echo ""
    echo "Команды:"
    echo "  start      - Запуск всех сервисов мониторинга"
    echo "  stop       - Остановка всех сервисов"
    echo "  restart    - Перезапуск сервисов"
    echo "  status     - Проверка статуса сервисов"
    echo "  logs       - Просмотр логов (опционально: имя сервиса)"
    echo "  cleanup    - Полная очистка с удалением данных"
    echo "  help       - Показать эту справку"
}

# Основной switch
case "${1:-help}" in
    start)
        start_services
        ;;
    stop)
        stop_services
        ;;
    restart)
        restart_services
        ;;
    status)
        show_status
        ;;
    logs)
        show_logs "${2:-}"
        ;;
    cleanup)
        cleanup_all
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        err "Неизвестная команда: $1"
        show_help
        exit 1
        ;;
esac
