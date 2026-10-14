# Дополнительные настройки PostgreSQL для повышения устойчивости
# Копировать в db/init-scripts/00-custom-pg-config.sh

# Создаём custom postgresql.conf overrides
# Эти настройки повышают устойчивость к неожиданным отключениям

cat << 'EOF' >> $PGDATA/postgresql.conf

# === НАСТРОЙКИ УСТОЙЧИВОСТИ ===

# Чаще сохранять данные на диск (по умолчанию 5 мин)
checkpoint_timeout = 1min

# Не ждать авто-чекпоинта при большой нагрузке
checkpoint_completion_target = 0.9

# Принудительный флейшинг каждого записи (безопасность vs скорость)
fsync = on
synchronous_commit = on

# Частота autoflush для меньших B-tree
autovacuum_vacuum_threshold = 50

# Меню максимальное время для одного checkpoint
max_wal_size = 2GB

# Как часто писать WAL на диск (по умолчанию 1 сек)
wal_sync_method = fdatasync

# Flushing data directory to disk after each transaction
full_page_writes = on

# Если включён, то при unexpected shutdown pg_recovery запустит auto
# уже включено по умолчанию в PG 16
EOF

echo "Custom postgresql.conf settings applied."
echo "Checkpoint now every 1 minute instead of 5 minutes."
