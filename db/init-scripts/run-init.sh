#!/bin/bash
# Скрипт для выполнения SQL инициализации вручную (если нужно)
# Обычно Docker автоматически выполняет SQL файлы в /docker-entrypoint-initdb.d

set -e

echo "Запуск инициализации базы данных..."

# Проверяем наличие переменных окружения
PGHOST="${PGHOST:-localhost}"
PGPORT="${PGPORT:-5432}"
PGDATABASE="${PGDATABASE:-artas_db}"
PGUSER="${PGUSER:-artas_user}"
PGPASSWORD="${PGPASSWORD:-artas_password}"

export PGPASSWORD

# Ждем готовности PostgreSQL
until psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -c '\q' 2>/dev/null; do
  echo "Ожидание PostgreSQL на $PGHOST:$PGPORT..."
  sleep 2
done

echo "PostgreSQL готов, выполняем SQL скрипты..."

# Выполняем SQL файлы по порядку
for sql_file in /docker-entrypoint-initdb.d/*.sql; do
    if [ -f "$sql_file" ]; then
        echo "Выполняем $sql_file"
        psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -f "$sql_file"
    fi
done

echo "Инициализация завершена."