# Управление сервисом записи фракталов (C#)

Этот документ описывает, как запускать и останавливать сервис записи фракталов
[`ArtasWriter.Worker`](../src/Writer/ArtasWriter.Worker) (.NET 10) в Windows.

## Скрипт запуска

Управление выполняется лаунчером [`run_Writer.ps1`](../src/Writer/run_Writer.ps1),
который собирает решение и запускает Writer (в фоне по умолчанию). Живость
проверяется через health-endpoint, остановка — graceful через `GET /exit`.

## Использование

Требуется разрешение на выполнение скриптов. Если скрипт не запускается, выполните в PowerShell:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Запуск сервиса
```powershell
.\src\Writer\run_Writer.ps1                 # сборка + запуск в фоне
.\src\Writer\run_Writer.ps1 -Foreground     # в текущем терминале
.\src\Writer\run_Writer.ps1 -NoBuild        # без пересборки (быстро)
.\src\Writer\run_Writer.ps1 -Simulate       # режим симуляции (без Finam)
```

### Остановка сервиса
```powershell
.\src\Writer\run_Writer.ps1 -Stop
```
Выполняет graceful shutdown через `GET /exit`.

**Примечание:** Сервис запускается отдельным процессом, поэтому закрытие консоли PowerShell
не останавливает Writer. Остановка — только через health-endpoint `/exit`.

## Сервис по умолчанию

Один процесс Writer обрабатывает **все** инструменты из `Writer:Symbols`
([`appsettings.json`](../src/Writer/ArtasWriter.Worker/appsettings.json)).

| Health-порт | Источник |
|-------------|----------|
| `8080` | `Writer:HealthPort` в `appsettings.json` |

## Ручное управление

Если лаунчер не подходит, можно запустить Writer напрямую:

### Запуск (форграунд)
```powershell
dotnet run --project src/Writer/ArtasWriter.Worker/ArtasWriter.Worker.csproj -c Release
```

### Запуск через лаунчер
```powershell
.\src\Writer\run_Writer.ps1                 # фон
.\src\Writer\run_Writer.ps1 -Foreground     # текущий терминал
.\src\Writer\run_Writer.ps1 -Simulate       # режим симуляции
.\src\Writer\run_Writer.ps1 -Stop           # graceful shutdown
```

### Остановка
```powershell
Invoke-RestMethod http://localhost:8080/exit
```
или найдите процесс:
```powershell
Get-Process dotnet | Where-Object { $_.Path -like "*dotnet*" }
```
и завершите его:
```powershell
Stop-Process -Id <PID> -Force
```

## Автозапуск при старте системы (Windows)

Чтобы сервис запускался автоматически при загрузке Windows, используйте Планировщик заданий:

1. Откройте `taskschd.msc`
2. Создайте задачу:
   - Триггер: "При запуске"
   - Действие: "Запустить программу"
   - Программа: `powershell.exe`
   - Аргументы: `-ExecutionPolicy Bypass -File "d:\work\Artas2026\src\Writer\run_Writer.ps1"`
   - Настройки: "Запускать при входе в систему"

или зарегистрируйте через реестр/скрипты репозитория (см. [`register_shutdown_task.ps1`](register_shutdown_task.ps1)).

## Мониторинг

- Логи сервиса пишутся в единый каталог `LOGS_DIR` (`<корень>/logs`, например
  `D:\work\Artas2026\logs`): `writer.stdout.log`, `writer.stderr.log`.
- Для просмотра логов в реальном времени:
  ```powershell
  Get-Content D:\work\Artas2026\logs\writer.stdout.log -Wait
  ```
- Логи собираются в **Loki** через Promtail (см. [`monitoring/README.md`](../monitoring/README.md)).

### Health endpoint

Сервис поднимает HTTP-эндпоинт `/health` (порт из `Writer.HealthPort`, по умолчанию `8080`):

- Порт задаётся в `appsettings.json` (`Writer.HealthPort`) или через переменную окружения `Writer__HealthPort`.
- Проверка через PowerShell:
  ```powershell
  Invoke-RestMethod http://localhost:8080/health
  ```
- Ответ — JSON с полями:
  - `status` — `"ok"`, если сервис жив;
  - `server_time` — текущее время сервера (UTC ISO 8601);
  - `symbols[]` — список инструментов, каждый с `last_tick` (`time_msc`, `time`, `last`, `bid`, `ask`, `volume`, `flags`, `interest`) и `fractals_count_today`.

Порт задаётся в `appsettings.json` (`Writer:HealthPort`, по умолчанию `8080`); при
необходимости переопределяется через переменную окружения `Writer__HealthPort`.

## Устранение неполадок

1. **Сервис не запускается** – проверьте, установлен ли .NET 10 SDK (`dotnet --version`).
2. **Ошибка сборки** – запустите `dotnet build src/Writer/ArtasWriter.slnx`.
3. **Процесс не останавливается** – используйте `Stop-Process -Id <PID> -Force` или диспетчер задач.
4. **Health недоступен** – проверьте, что порт не занят другим процессом (см. [`kill_by_ports.ps1`](kill_by_ports.ps1)).

## Примечания

- Список инструментов задаётся в `appsettings.json` (`Writer.Symbols`), а не CLI-параметрами.
- Сервис подключается к базе данных PostgreSQL; убедитесь, что БД запущена и доступна.
- Graceful shutdown выполняется через `GET /exit`.
