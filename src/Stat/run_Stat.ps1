# ArtasStat launcher (.NET 10, batch-job)
#
# Usage:
#   .\run_Stat.ps1                                       # build Release + run
#   .\run_Stat.ps1 -Symbol USDRUBF -StopPoint 3 -TakePoint 20
#   .\run_Stat.ps1 -NoBuild                              # run without rebuild (fast)
#   .\run_Stat.ps1 -Clear                                # очистить stat перед прогоном
#   .\run_Stat.ps1 -NoPause                              # не ждать нажатия клавиши
#
# Stat — batch-job: выполняет полный прогон расчёта статистики и завершается
# (exit code 0 — успех, 1 — ошибка). Health-эндпоинтов нет.
#
# Приложение запускается в текущем терминале, логи пишутся прямо в консоль.

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Symbol = "Si",
    [int]$StopPoint = 3,
    [int]$TakePoint = 20,
    [int]$MaxDeep = 100,
    [switch]$Clear,
    [switch]$NoBuild,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# Корень репозитория (src/Stat -> ../..). База для .env и fallback-каталог логов.
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..\..")
$Solution = Join-Path $ScriptDir "Stat.slnx"
$Project = Join-Path $ScriptDir "Stat.Worker\Stat.Worker.csproj"

# --- Выход с ожиданием клавиши --------------------------------
# При запуске "Выполнить с помощью PowerShell" из проводника окно
# создаётся хостом powershell.exe и закрывается сразу после завершения
# скрипта. Ожидание реализовано внешним `cmd /c pause` — он гарантированно
# блокируется до нажатия клавиши. Пауза отключается флагом -NoPause.
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

# --- .env загрузка --------------------------------------------
# Ищем .env рядом со скриптом, затем в корне репозитория и загружаем
# пары KEY=VALUE в переменные окружения процесса (пароль БД и т.п.).
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

# --- Аргументы приложения -------------------------------------
$appArgs = @(
    "--symbol", $Symbol,
    "--stop-point", "$StopPoint",
    "--take-point", "$TakePoint",
    "--max-deep", "$MaxDeep"
)
if ($Clear) { $appArgs += "--clear" }

$dotnetArgs = @("run", "--project", $Project, "-c", $Configuration, "--no-build", "--") + $appArgs

# --- Каталог логов --------------------------------------------
# Каталог логов берём из переменной LOGS_DIR (.env/окружение), иначе — <root>/logs.
# Единый каталог используется Promtail/Loki. Stat — batch-job, поэтому пишем
# один файл stat.<Symbol>.log (stdout+stderr вместе) и дублируем в консоль.
$logDir = if ($env:LOGS_DIR) { $env:LOGS_DIR } else { Join-Path $ProjectRoot "logs" }
New-Item -ItemType Directory -Force $logDir | Out-Null
$logFile = Join-Path $logDir "stat.$Symbol.log"

# --- Run ------------------------------------------------------
# Запускаем в текущем терминале; вывод дублируется в файл через Tee-Object.
Write-Host "[run] Starting ArtasStat ($Configuration) (Symbol=$Symbol)..." -ForegroundColor Green
Write-Host "[run] Логи: $logFile" -ForegroundColor DarkGray
$exitCode = 0
try {
    & $dotnet @dotnetArgs 2>&1 | Tee-Object -FilePath $logFile
    $exitCode = $LASTEXITCODE
} catch {
    Write-Host "[run] Ошибка запуска: $_" -ForegroundColor Red
    $exitCode = 1
}

Exit-WithPause $exitCode
