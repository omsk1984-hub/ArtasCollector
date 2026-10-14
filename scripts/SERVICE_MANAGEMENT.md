# Управление сервисом записи фракталов (C#)

Этот документ описывает, как запускать и останавливать сервис записи фракталов
[`ArtasWriter.Worker`](../src/Writer/ArtasWriter.Worker) (.NET 10) в Windows.

## Скрипты управления

В папке `scripts` создан PowerShell-скрипт:

- `manage_fractal_writer.ps1` — управление сервисом (требует PowerShell 5+).

Дополнительно в репозитории есть лаунчер [`run_Writer.ps1`](../src/Writer/run_Writer.ps1),
который собирает решение и запускает Writer (в фоне по умолчанию).

## Использование

Требуется разрешение на выполнение скриптов. Если скрипт не запускается, выполните в PowerShell:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Запуск сервиса
```powershell
.\manage_fractal_writer.ps1 start CNYRUBF -HealthPort 8080
```
Скрипт собирает решение (если не указан `-NoBuild`) и запускает `dotnet run` в фоне
(`-WindowStyle Hidden`). Живость проверяется через health-endpoint.

### Остановка сервиса
```powershell
.\manage_fractal_writer.ps1 stop CNYRUBF
```
Выполняет graceful shutdown через `GET /exit`.

### Перезапуск сервиса
```powershell
.\manage_fractal_writer.ps1 restart CNYRUBF
```

### Проверка статуса
```powershell
.\manage_fractal_writer.ps1 status
.\manage_fractal_writer.ps1 all-status
```

### Массовые действия
```powershell
.\manage_fractal_writer.ps1 all-start
.\manage_fractal_writer.ps1 all-stop
.\manage_fractal_writer.ps1 all-restart
```

**Примечание:** Сервис запускается отдельным процессом, поэтому закрытие консоли PowerShell
не останавливает Writer. Остановка — только через health-endpoint `/exit`.

## Список сервисов по умолчанию

| Символ | Health-порт |
|--------|-------------|
| `CNYRUBF` | `8080` |
| `USDRUBF` | `8081` |
| `Si` | `8082` |

## Ручное управление

Если скрипт не работает, можно запустить Writer напрямую:

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
   - Аргументы: `-ExecutionPolicy Bypass -File "d:\work\Artas2026\scripts\manage_fractal_writer.ps1" start CNYRUBF -HealthPort 8080`
   - Настройки: "Запускать при входе в систему"

или зарегистрируйте через реестр/скрипты репозитория (см. [`register_shutdown_task.ps1`](register_shutdown_task.ps1)).

## Мониторинг

- Логи сервиса пишутся в `src/Writer/logs/` (`writer.<port>.stdout.log`, `writer.<port>.stderr.log`).
- Для просмотра логов в реальном времени:
  ```powershell
  Get-Content src/Writer/logs/writer.8080.stdout.log -Wait
  ```
- Структурированные логи могут отправляться в **Loki** (см. [`monitoring/README.md`](../monitoring/README.md)).

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

При управлении через `manage_fractal_writer.ps1` порт передаётся параметром `-HealthPort`:
```powershell
.\manage_fractal_writer.ps1 start CNYRUBF -HealthPort 8080
```
Команда `status` сама проверяет health endpoint и выводит результат.

## Устранение неполадок

1. **Сервис не запускается** – проверьте, установлен ли .NET 10 SDK (`dotnet --version`).
2. **Ошибка сборки** – запустите `dotnet build src/Writer/ArtasWriter.slnx`.
3. **Процесс не останавливается** – используйте `Stop-Process -Id <PID> -Force` или диспетчер задач.
4. **Health недоступен** – проверьте, что порт не занят другим процессом (см. [`kill_by_ports.ps1`](kill_by_ports.ps1)).

## Примечания

- Список инструментов задаётся в `appsettings.json` (`Writer.Symbols`), а не CLI-параметрами.
- Сервис подключается к базе данных PostgreSQL; убедитесь, что БД запущена и доступна.
- Graceful shutdown выполняется через `GET /exit`.
