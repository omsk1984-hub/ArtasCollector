# Используем официальный образ PostgreSQL
FROM postgres:16-alpine

# Устанавливаем переменные окружения для базы данных по умолчанию
ENV POSTGRES_DB=artas_db
ENV POSTGRES_USER=artas_user
ENV POSTGRES_PASSWORD=artas_password

# Копируем ТОЛЬКО конфигурационный скрипт повышения устойчивости PostgreSQL.
# Схема БД (таблицы, партиции, индексы) больше НЕ создаётся здесь:
# она управляется EF Core миграциями проекта src/Core/ArtasCore и применяется
# отдельным шагом через db/apply-migrations.ps1 (`dotnet ef database update`).
COPY init-scripts/00-custom-pg-config.sh /docker-entrypoint-initdb.d/

# Устанавливаем права на выполнение скриптов
RUN chmod +x /docker-entrypoint-initdb.d/*.sh 2>/dev/null || true

# Экспонируем порт PostgreSQL
EXPOSE 5432

# Комментарий: стандартный entrypoint postgres уже запускает сервер
