# ArtasWriter launcher (.NET 10)
#
# Usage:
#   .\run.ps1                          # build Release + run (фон, по умолчанию)
#   .\run.ps1 -Foreground              # запустить в текущем терминале (foreground)
#   .\run.ps1 -Configuration Debug     # другая конфигурация
#   .\run.ps1 -NoBuild                 # run without rebuild (fast)
#   .\run.ps1 -Simulate                # force SimulationMode=true (без реального Finam)
#   .\run.ps1 -Background              # явно фоновый запуск (поведение по умолчанию)
#   .\run.ps1 -Background -NoBuild     # фоновый запуск без пересборки
#   .\run.ps1 -Stop                    # остановить фоновый процесс (через /exit)
#   .\run.ps1 -NoPause                 # не ждать нажатия клавиши в конце
#
# По умолчанию приложение стартует отдельным процессом, поэтому закрытие
# текущей консоли не останавливает Writer. Готовность проверяется по порту
# health-эндпоинта, остановка — graceful через /exit. Для запуска в текущем
# терминале используйте -Foreground.
#
# Скрипт рассчитан в том числе на запуск двойным кликом / через
# "Выполнить с помощью PowerShell" из проводника. В этом случае explorer
# создаёт отдельное окно powershell.exe, которое закрывается сразу после
# завершения скрипта. Чтобы окно не исчезало мгновенно, перед выходом скрипт
# ждёт нажатия клавиши (отключается флагом -NoPause). При запуске из уже
# открытого терминала (или из CI) пауза не выполняется автоматически.

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$Simulate,
    [switch]$Background,
    [switch]$Foreground,
    [switch]$Stop,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# Корень репозитория (src/Writer -> ../..). Используется как база для .env и
# как fallback-каталог логов, если в .env не задан LOGS_DIR.
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..\..")
$Solution = Join-Path $ScriptDir "ArtasWriter.slnx"
$Project = Join-Path $ScriptDir "ArtasWriter.Worker\ArtasWriter.Worker.csproj"
$HealthPort = 8080

# --- Выход с ожиданием клавиши --------------------------------
# Проблема: при запуске "Выполнить с помощью PowerShell" из проводника окно
# создаётся хостом powershell.exe и закрывается сразу после завершения
# скрипта. Внутренние Read-Host/ReadKey в этом режиме ненадёжны, поэтому
# ожидание реализовано внешним процессом `cmd /c pause` — он гарантированно
# блокируется до нажатия клавиши и не даёт хосту завершиться.
# Пауза отключается флагом -NoPause (например, для автоматических запусков).
function Exit-WithPause([int]$Code) {
    if (-not $NoPause) {
        Write-Host ""
        Write-Host "[run] Нажмите любую клавишу для выхода..." -ForegroundColor DarkGray
        try {
            & cmd.exe /c "pause > nul"
        } catch {
            try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch { }
        }
    }
    exit $Code
}

if (-not (Test-Path $Solution)) {
    Write-Error "Solution not found: $Solution"
    Exit-WithPause 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
    if (-not (Test-Path $dotnet)) {
        Write-Error "dotnet CLI not found. Install .NET 10 SDK: https://aka.ms/dotnet/download"
        Exit-WithPause 1
    }
} else {
    $dotnet = "dotnet"
}

# Проверка, слушается ли health-порт.
function Test-WriterPort {
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $iar = $client.BeginConnect("127.0.0.1", $HealthPort, $null, $null)
        $ok = $iar.AsyncWaitHandle.WaitOne(500)
        if ($ok) { $client.EndConnect($iar) }
        $client.Close()
        return $ok
    } catch {
        return $false
    }
}

# --- Остановка фонового процесса ---------------------------------
if ($Stop) {
    if (-not (Test-WriterPort)) {
        Write-Host "[run] Writer не запущен (порт $HealthPort свободен)." -ForegroundColor DarkGray
        Exit-WithPause 0
    }

    try {
        Invoke-RestMethod "http://localhost:$HealthPort/exit" -TimeoutSec 5 | Out-Null
        Write-Host "[run] Отправлен graceful shutdown (/exit)" -ForegroundColor Cyan
    } catch {
        Write-Error "Не удалось вызвать /exit: $_"
        Exit-WithPause 1
    }

    # Ждём освобождения порта (до ~15 с).
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        if (-not (Test-WriterPort)) {
            Write-Host "[run] Writer остановлен." -ForegroundColor Green
            Exit-WithPause 0
        }
    }

    Write-Error "Порт $HealthPort всё ещё занят после /exit."
    Exit-WithPause 1
}

# --- .env загрузка --------------------------------------------
# Ищем .env рядом со скриптом, затем в корне репозитория и загружаем
# пары KEY=VALUE в переменные окружения процесса. Секреты (Finam__Token)
# задавать здесь, а не в appsettings.json.
$envCandidates = @(
    (Join-Path $ScriptDir ".env"),
    (Join-Path $ScriptDir "..\..\..\.env"),
    (Join-Path (Get-Location) ".env")
)
foreach ($envFile in $envCandidates) {
    if (Test-Path $envFile) {
        Write-Host "[run] Loading env file: $envFile" -ForegroundColor DarkGray
        Get-Content $envFile | ForEach-Object {
            $line = $_.Trim()
            if ($line -eq "" -or $line.StartsWith("#")) { return }
            $idx = $line.IndexOf("=")
            if ($idx -lt 1) { return }
            $key = $line.Substring(0, $idx).Trim()
            $value = $line.Substring($idx + 1).Trim().Trim('"').Trim("'")
            # Не перезаписываем уже заданные в окружении переменные.
            if (-not [System.Environment]::GetEnvironmentVariable($key, "Process")) {
                [System.Environment]::SetEnvironmentVariable($key, $value, "Process")
            }
        }
        break
    }
}

# --- Build ----------------------------------------------------
if (-not $NoBuild) {
    Write-Host "[run] Building solution ($Configuration)..." -ForegroundColor Cyan
    & $dotnet build $Solution -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed with code $LASTEXITCODE"
        Exit-WithPause $LASTEXITCODE
    }
}

# --- Simulation mode ------------------------------------------
if ($Simulate) {
    $env:Finam__SimulationMode = "true"
    Write-Host "[run] SimulationMode=true (без реального Finam)" -ForegroundColor Yellow
}

# Формируем список аргументов для `dotnet run` (общий для обоих режимов).
$dotnetArgs = @("run", "--project", $Project, "-c", $Configuration, "--no-build")

# --- Защита от повторного запуска -----------------------------
# Порт health-эндпоинта служит уникальным ресурсом: если он занят, значит
# Writer уже запущен. Второй экземпляр писать в ту же БД нельзя — при
# параллельной работе каждый процесс держит собственный TickService, и первый
# фрактал после старта задаёт базу интереса дня = своему интересу, что даёт
# некорректный from_start_interest = 0 (см. db/recalculate-from-start-interest.sql).
if (Test-WriterPort) {
    Write-Host "[run] Writer уже запущен (порт $HealthPort занят)." -ForegroundColor Yellow
    Write-Host "[run] Повторный запуск отменён. Чтобы перезапустить:" -ForegroundColor DarkGray
    Write-Host "[run]   .\run_Writer.ps1 -Stop   # остановить текущий" -ForegroundColor DarkGray
    Exit-WithPause 0
}

# --- Background mode ------------------------------------------
# По умолчанию запускаем в фоне; -Foreground отключает фоновый режим.
$runBackground = -not $Foreground
if ($runBackground) {
    Write-Host "[run] Starting ArtasWriter ($Configuration) в фоне..." -ForegroundColor Green

    # Каталог логов берём из переменной LOGS_DIR (.env/окружение), иначе —
    # <root>/logs. Единый каталог используется Promtail/Loki.
    $logDir = if ($env:LOGS_DIR) { $env:LOGS_DIR } else { Join-Path $ProjectRoot "logs" }
    New-Item -ItemType Directory -Force $logDir | Out-Null
    $maxAge = 7 * 24 * 60 * 60  # 7 дней в секундах
    $now = Get-Date
    Get-ChildItem $logDir -Filter "*.log" | Where-Object {
        ($now - $_.LastWriteTime).TotalSeconds -gt $maxAge
    } | Remove-Item -Force -ErrorAction SilentlyContinue

    # Перенаправление stdout/stderr в файлы (чтобы логи не терялись).
    $stdoutLog = Join-Path $logDir "writer.stdout.log"
    $stderrLog = Join-Path $logDir "writer.stderr.log"

    $proc = Start-Process -FilePath $dotnet `
        -ArgumentList $dotnetArgs `
        -WorkingDirectory $ScriptDir `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -PassThru

    # Ждём, пока поднимется health-порт (до ~15 с).
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { break }
        if (Test-WriterPort) { $ready = $true; break }
    }

    if ($proc.HasExited) {
        Write-Host "[run] Процесс завершился сразу (код $($proc.ExitCode))." -ForegroundColor Red
        Exit-WithPause 1
    }

    if (-not $ready) {
        Write-Host "[run] Порт $HealthPort не поднялся за отведённое время." -ForegroundColor Red
        Exit-WithPause 1
    }

    Write-Host "[run] ArtasWriter запущен в фоне." -ForegroundColor Green
    Write-Host "[run] Health:    http://localhost:$HealthPort/health" -ForegroundColor DarkGray
    Write-Host "[run] Остановка: http://localhost:$HealthPort/exit  (или: .\run_Writer.ps1 -Stop)" -ForegroundColor DarkGray

    try {
        $snap = Invoke-RestMethod "http://localhost:$HealthPort/health" -TimeoutSec 5
        Write-Host "[run] Статус:" -ForegroundColor DarkGray
        $snap | ConvertTo-Json -Depth 4
    } catch { }

    Exit-WithPause 0
}

# --- Foreground mode ------------------------------------------
Write-Host "[run] Starting ArtasWriter ($Configuration) в текущем терминале..." -ForegroundColor Green
$exitCode = 0
try {
    & $dotnet @dotnetArgs
    $exitCode = $LASTEXITCODE
} catch {
    Write-Host "[run] Ошибка запуска: $_" -ForegroundColor Red
    $exitCode = 1
}

Exit-WithPause $exitCode
