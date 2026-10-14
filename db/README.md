# Развертывание PostgreSQL через Docker

Эта папка содержит конфигурацию для развертывания PostgreSQL в Docker контейнере для проекта Artas2026.

## Структура

- `Dockerfile` - Docker образ PostgreSQL с предустановленными настройками
- `docker-compose.yml` - конфигурация для запуска контейнера с помощью Docker Compose
- `apply-migrations.ps1` - применение EF Core миграций (`dotnet ef database update`)
- `ef-baseline.sql` - baseline для уже существующей БД (пометка миграций применёнными)
- `init-scripts/` - конфигурация и сервисные скрипты PG:
  - `00-custom-pg-config.sh` - настройки устойчивости PostgreSQL (применяется образом)
  - `artas-autostart.sh`, `run-init.sh`, `*.service`, `*-stop.sh` - сервисные скрипты WSL
- `init-scripts/archive/` - архив прежних SQL-миграций; логика перенесена в EF Core
- `database_schema.md` - описание схемы БД и работы с миграциями

## Миграции схемы (EF Core)

Схема БД управляется **EF Core миграциями** в проекте [`ArtasCore`](../src/Core/ArtasCore),
а не SQL‑скриптами. Применение:

```powershell
# Применить миграции к БД (по умолчанию artas_db)
.\db\apply-migrations.ps1

# Список миграций
.\db\apply-migrations.ps1 -List

# Baseline для уже существующей БД (не выполнять DDL, только пометить применёнными)
.\db\apply-migrations.ps1 -Baseline
```

Для чистой БД достаточно `docker-compose up -d postgres` (контейнер не создаёт схему),
затем `.\db\apply-migrations.ps1`.

## Быстрый старт

### Предварительные требования

- Установленный Docker и Docker Compose
- Доступ к порту 5432 (или другому, если измените настройки)

### Запуск базы данных

1. Перейдите в папку `db`:
   ```bash
   cd db
   ```

2. Запустите контейнеры:
   ```bash
   docker-compose up -d
   ```

3. Проверьте статус:
   ```bash
   docker-compose ps
   ```

4. Остановка контейнеров:
   ```bash
   docker-compose down
   ```

### Переменные окружения

Вы можете настроить параметры базы данных через переменные окружения или файл `.env`:

```bash
# Пример .env файла
POSTGRES_DB=artas_db
POSTGRES_USER=artas_user
POSTGRES_PASSWORD=artas_password
POSTGRES_PORT=5432
```

## Подключение к базе данных

### Из командной строки

```bash
# Используя psql (если установлен)
psql -h localhost -p 5432 -U artas_user -d artas_db

# Пароль: artas_password
```

### Из C# приложения

Пример подключения с использованием `Npgsql`:

```csharp
var connString = "Host=localhost;Port=5432;Database=artas_db;Username=artas_user;Password=artas_password";
await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();
```

### Из Docker контейнера

```bash
# Войти в контейнер
docker exec -it artas_postgres bash

# Подключиться к PostgreSQL внутри контейнера
psql -U artas_user -d artas_db
```

## Данные по умолчанию

Схема создаётся **EF Core миграциями** (не автоматически образом). После
`docker compose up -d postgres` требуется применить миграции:

```powershell
.\db\apply-migrations.ps1
```

1. **Таблицы**:
   - `symbols` - финансовые инструменты
   - `fractals` - обнаруженные фракталы
   - `symbol_filter_settings` - настройки фильтра фракталов по символам

2. **Начальные данные** (сиды миграции):
   - Символы: `Si`, `MX` (MOEX max, MXZ6), `IMOEXF` (MOEX),
     `CNY` (CRZ6), `USDRUBF`, `CNYRUBF`, `MM` (MOEX mini, MMZ6), `RI` (RTS, RIZ6)
   - Эти же инструменты перечислены в `Writer:Symbols` ([`appsettings.json`](../src/Writer/ArtasWriter.Worker/appsettings.json)).

## Управление томами

Данные PostgreSQL сохраняются в Docker volume `postgres_data`. Чтобы удалить данные полностью:

```bash
docker-compose down -v
```

## Мониторинг и логи

- Просмотр логов контейнера:
  ```bash
  docker-compose logs -f postgres
  ```

- Проверка здоровья:
  ```bash
  docker-compose exec postgres pg_isready -U artas_user -d artas_db
  ```

## Интеграция с проектом Artas2026

Для использования базы данных в проекте:

1. Доступ к данным реализован через `Npgsql`/EF Core в общих проектах
   ([`ArtasCore`](../src/Core/ArtasCore)), конфигурация — `DbConfig`.

2. Настройте строку подключения в `appsettings.json` (секция `Db`) или через переменные
   окружения (`DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD`).

3. Используйте символ `CNYRUBF` для тестирования.

## Решение проблем

### Порт уже занят

Измените `POSTGRES_PORT` в `.env` файле или отредактируйте `docker-compose.yml`.

### Ошибка прав доступа

Убедитесь, что скрипты в `init-scripts/` имеют права на выполнение:
```bash
chmod +x init-scripts/*.sh
```

### Контейнер не запускается

Проверьте логи:
```bash
docker-compose logs postgres
```

## Дополнительные настройки

Для production использования рекомендуется:

1. Изменить пароли на более сложные
2. Настроить резервное копирование
3. Включить SSL соединения
4. Настроить репликацию

## Лицензия

Конфигурация Docker является частью проекта Artas2026.