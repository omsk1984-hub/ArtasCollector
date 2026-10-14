# Инструкция: запуск проекта Artas2026

> Проект: **Artas2026** (C# / .NET 10, PostgreSQL в Docker/WSL, Avalonia + ScottPlot).
> Все пути — относительно корня репозитория `d:/work/Artas2026`.

Это пошаговое руководство: как подготовить окружение, поднять БД, запустить
сервисы (Writer, Stat, Reader) и как правильно **перекомпилировать** проект.

---

## ⚠️ Главное правило: перекомпиляция

> **Перед перекомпиляцией ОБЯЗАТЕЛЬНО спросите пользователя, нужно ли остановить
> запущенные сервисы.**

Почему это важно:

- Запущенный **Writer** держит свои DLL из `bin\<Configuration>\net10.0` открытыми.
  Сборка (или копирование выходных файлов) упадёт с ошибками блокировки:

  ```
  error MSB3021: не удалось скопировать файл "...\ArtasWriter.Worker.dll" ...
  error MSB3027: Превышено допустимое число повторных попыток (10) ...
  The process cannot access the file ... because it is being used by another process.
  ```

- Запущенный **Reader** (ArtasChart) держит DLL из `bin\Release`, поэтому его
  лаунчер [`run_Reader.ps1`](../src/Reader/run_Reader.ps1) **намеренно пропускает
  пересборку**, пока есть открытые окна («пересборка пропущена, чтобы не
  блокировать их DLL»).

- Запущенный **Stat** — batch-job: он завершается сам, но во время прогона тоже
  держит DLL.

### Как правильно

1. **Спросить пользователя**, можно ли остановить сервисы для пересборки.
2. Если да — корректно остановить (Writer: `-Stop` через `/exit`; Reader: закрыть окна).
3. Выполнить сборку / тесты.
4. Запустить сервисы заново.

> Никогда не запускайте пересборку «вслепую», пока сервисы работают: это либо
> упадёт с `MSB3027`, либо (как с Reader) молча не пересоберётся.

---

## 1. Предварительные требования

| Компонент | Назначение | Проверка |
|-----------|------------|----------|
| **.NET 10 SDK** | сборка и запуск всех сервисов | `dotnet --version` |
| **Docker в WSL** | PostgreSQL и стек мониторинга | `wsl bash -c "docker ps"` |
| **WSL (Ubuntu)** | хост для Docker | `wsl -l -v` |
| **EF Core CLI** | применение миграций | `dotnet ef --version` |

Установка .NET SDK: https://aka.ms/dotnet/download

---

## 2. Переменные окружения

Скопируйте пример и отредактируйте:

```powershell
Copy-Item .env.example .env
```

Основные переменные:

| Переменная | Назначение | По умолчанию |
|------------|------------|--------------|
| `DB_HOST` / `DB_PORT` | адрес и порт PostgreSQL | `127.0.0.1` / `5432` |
| `DB_NAME` / `DB_USER` / `DB_PASSWORD` | доступ к БД | `artas_db` / `artas_user` / `artas_password` |
| `Finam__Token` | секретный ключ Finam (`tapi_sk_...`) | — |
| `LOGS_DIR` | каталог логов (для Promtail/Loki) | `<root>/logs` |

> Секрет `Finam__Token` задавайте **только** через `.env` / переменную окружения,
> **не** коммитьте его в `appsettings.json`.
> Лаунчеры [`run_Writer.ps1`](../src/Writer/run_Writer.ps1) и
> [`run_Stat.ps1`](../src/Stat/run_Stat.ps1) автоматически загружают `.env`
> (рядом со скриптом, в корне репозитория или в текущем каталоге).

---

## 3. Запуск базы данных (PostgreSQL в Docker/WSL)

Docker работает внутри WSL, поэтому контейнеры поднимаются через WSL:

```powershell
wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d"
```

Проверка:

```powershell
wsl bash -c "docker ps"
```

Схема БД управляется **EF Core миграциями** (см. [`db/README.md`](../db/README.md)).
Применить миграции:

```powershell
.\db\apply-migrations.ps1            # применить к artas_db
.\db\apply-migrations.ps1 -List      # список миграций
```

---

## 4. Сборка проекта

> Перед сборкой убедитесь, что сервисы остановлены (см. раздел «Главное правило»).

Сборка всех решений:

```powershell
dotnet build src/Core/ArtasCore/ArtasCore.csproj
dotnet build src/Writer/ArtasWriter.slnx
dotnet build src/Stat/Stat.slnx
dotnet build src/Reader/ArtasChart.sln
```

Запуск тестов (через `dotnet test` — по правилам проекта):

```powershell
dotnet test src/Writer/ArtasWriter.slnx
dotnet test src/Stat/Stat.slnx
dotnet test src/Reader/ArtasChart.sln
```

---

## 5. Запуск сервисов

### 5.1. Writer — запись фракталов (Finam → PostgreSQL)

Один процесс обрабатывает все инструменты из `Writer:Symbols`
([`appsettings.json`](../src/Writer/ArtasWriter.Worker/appsettings.json)).
Health-порт — `Writer:HealthPort` (по умолчанию `8080`).

```powershell
.\src\Writer\run_Writer.ps1                 # сборка Release + запуск в фоне
.\src\Writer\run_Writer.ps1 -Foreground     # запуск в текущем терминале
.\src\Writer\run_Writer.ps1 -Simulate       # режим симуляции (без реального Finam)
.\src\Writer\run_Writer.ps1 -NoBuild        # запуск без пересборки
.\src\Writer\run_Writer.ps1 -Stop           # graceful shutdown через /exit
```

Проверка и остановка:

```powershell
Invoke-RestMethod http://localhost:8080/health
Invoke-RestMethod http://localhost:8080/exit
```

> **Важно:** второй экземпляр Writer на тот же health-порт запустить нельзя —
> порт служит защитой от двойного запуска (два процесса испортят базу интереса дня).

### 5.2. Reader — визуализация (Avalonia + ScottPlot)

```powershell
.\src\Reader\run_Reader.ps1                  # сборка Release + GUI (в фоне)
.\src\Reader\run_Reader.ps1 -Foreground      # GUI в текущем терминале
.\src\Reader\run_Reader.ps1 -CheckDb Si      # только проверка БД, без GUI
.\src\Reader\run_Reader.ps1 -Symbol CNYRUBF  # GUI для конкретного символа
```

> Можно открывать сколько угодно окон. Если хотя бы одно окно запущено, лаунчер
> **не пересобирает** проект (чтобы не блокировать его DLL) и открывает новое
> окно из текущего билда. Чтобы пересобрать — закройте все окна Reader.

### 5.3. Stat — расчёт статистики фракталов (batch-job)

Выполняет полный прогон и завершается (`exit code 0` — успех, `1` — ошибка).
Health-эндпоинтов нет.

```powershell
.\src\Stat\run_Stat.ps1                                           # по умолчанию Symbol=Si
.\src\Stat\run_Stat.ps1 -Symbol USDRUBF -StopPoint 3 -TakePoint 20
.\src\Stat\run_Stat.ps1 -Clear                                    # очистить stat перед прогоном
.\src\Stat\run_Stat.ps1 -NoBuild                                  # без пересборки
```

Логи пишутся в `logs/stat.<Symbol>.log` и дублируются в консоль.

---

## 6. Опционально: стек мониторинга

Loki + Promtail + Prometheus + OTel Collector поднимаются отдельно
(см. [`monitoring/README.md`](../monitoring/README.md)):

```powershell
wsl bash -c "cd /mnt/d/work/Artas2026 && ./scripts/manage_monitoring.sh start"
```

Loki API — http://localhost:3100.

---

## 7. Остановка системы

```powershell
.\src\Writer\run_Writer.ps1 -Stop           # остановить Writer (грациозно)
.\scripts\safe_shutdown.ps1                 # остановить Writer'ы + PostgreSQL
.\scripts\safe_shutdown.ps1 --wsl-shutdown  # + закрыть WSL
```

Reader закрывается обычным закрытием окна. Stat завершается сам.

---

## Шпаргалка команд

```powershell
# БД
wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d"
.\db\apply-migrations.ps1

# Writer
.\src\Writer\run_Writer.ps1
.\src\Writer\run_Writer.ps1 -Stop

# Reader
.\src\Reader\run_Reader.ps1

# Stat
.\src\Stat\run_Stat.ps1 -Symbol Si

# Тесты
dotnet test src/Writer/ArtasWriter.slnx
dotnet test src/Stat/Stat.slnx
dotnet test src/Reader/ArtasChart.sln
```

---

## Чек-лист перекомпиляции

- [ ] Спросить пользователя, нужно ли остановить запущенные сервисы.
- [ ] Остановить Writer (`run_Writer.ps1 -Stop`) и/или закрыть окна Reader.
- [ ] Убедиться, что процессы завершены (health-порт свободен / окон нет).
- [ ] Выполнить `dotnet build` / `dotnet test`.
- [ ] Запустить сервисы заново нужным лаунчером.
