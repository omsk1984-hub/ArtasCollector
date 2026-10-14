# Применение EF Core миграций к базе данных Artas2026.
#
# Использование:
#   .\apply-migrations.ps1                     # применить миграции к artas_db (по умолчанию)
#   .\apply-migrations.ps1 -Baseline           # пометить миграции применёнными БЕЗ выполнения (для уже существующей БД)
#   .\apply-migrations.ps1 -Database artas_x   # применить к другой базе
#   .\apply-migrations.ps1 -List               # показать список миграций
#
# Параметры подключения по умолчанию: 127.0.0.1:5432/artas_db (artas_user).
# Переопределяются переменными окружения DB_HOST/DB_PORT/DB_NAME/DB_USER/DB_PASSWORD.

param(
    [string]$Database = "artas_db",
    [string]$Host_    = $env:DB_HOST,
    [int]   $Port     = 0,
    [string]$User     = $env:DB_USER,
    [string]$Password = $env:DB_PASSWORD,
    [switch]$Baseline,
    [switch]$List
)

$ErrorActionPreference = "Stop"

if (-not $Host_)    { $Host_    = "127.0.0.1" }
if ($Port -eq 0)    { $Port     = 5432 }
if (-not $User)     { $User     = "artas_user" }
if (-not $Password) { $Password = "artas_password" }

$repoRoot    = Split-Path -Parent $PSScriptRoot
$coreProject = Join-Path $repoRoot "src\Core\ArtasCore\ArtasCore.csproj"

if (-not (Test-Path $coreProject)) {
    Write-Host "[ОШИБКА] Не найден проект ArtasCore: $coreProject" -ForegroundColor Red
    exit 1
}

$connectionString =
    "Host=$Host_;Port=$Port;Database=$Database;Username=$User;Password=$Password;Timezone=UTC"

Write-Host "EF Core миграции Artas2026" -ForegroundColor Cyan
Write-Host "  База:    $Database" -ForegroundColor Gray
Write-Host "  Сервер:  ${Host_}:$Port" -ForegroundColor Gray
Write-Host ""

if ($List) {
    Write-Host "Список миграций:" -ForegroundColor Cyan
    $env:ARTAS_MIGRATIONS_CONNECTION = $connectionString
    dotnet ef migrations list `
        --project $coreProject `
        --startup-project $coreProject `
        --context ArtasDbContext
    exit $LASTEXITCODE
}

if ($Baseline) {
    Write-Host "Baseline: пометка миграций применёнными (без выполнения DDL)..." -ForegroundColor Yellow
    $env:ARTAS_MIGRATIONS_CONNECTION = $connectionString
    $efArgs = @(
        "ef", "database", "update",
        "--project", $coreProject,
        "--startup-project", $coreProject,
        "--context", "ArtasDbContext"
    )
    # Создаём таблицу истории и заполняем её через SQL скрипт baseline.
    $baselineSql = Join-Path $PSScriptRoot "ef-baseline.sql"

    if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
        Write-Host "psql не найден в PATH. Вариант через Docker:" -ForegroundColor Yellow
        Write-Host "  docker exec -i artas_postgres psql -U $User -d $Database < db\ef-baseline.sql" -ForegroundColor Gray
        exit 1
    }

    $env:PGPASSWORD = $Password
    psql -h $Host_ -p $Port -U $User -d $Database -f $baselineSql
    exit $LASTEXITCODE
}

Write-Host "Применение миграций..." -ForegroundColor Cyan
$env:ARTAS_MIGRATIONS_CONNECTION = $connectionString
dotnet ef database update `
    --project $coreProject `
    --startup-project $coreProject `
    --context ArtasDbContext

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nМиграции успешно применены." -ForegroundColor Green
} else {
    Write-Host "`n[ОШИБКА] Применение миграций не удалось (код $LASTEXITCODE)." -ForegroundColor Red
}
exit $LASTEXITCODE
