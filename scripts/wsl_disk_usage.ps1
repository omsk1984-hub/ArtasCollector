# Анализ занятого места в WSL
# Использование: .\wsl_disk_usage.ps1
# Опции:
#   .\wsl_disk_usage.ps1 -Top 30        # Показать Top-N самых больших каталогов (по умолчанию 20)
#   .\wsl_disk_usage.ps1 -Path /root    # Анализировать конкретный путь (по умолчанию /)
#   .\wsl_disk_usage.ps1 -NoDocker      # Пропустить анализ Docker
#
# Скрипт показывает:
#   1. Реальный размер виртуального диска WSL (ext4.vhdx) на хосте Windows
#   2. Использование файловой системы внутри WSL (df -h)
#   3. Самые большие каталоги внутри WSL (du)
#   4. Самые большие файлы внутри WSL
#   5. Использование Docker (docker system df — образы, контейнеры, volumes, build cache)
#   6. Подсказки по освобождению места

param(
    [int]$Top = 20,
    [string]$Path = "/",
    [switch]$NoDocker
)

$ErrorActionPreference = "Continue"

function Write-Section($msg) {
    Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

function Test-WslAvailable {
    try {
        $null = wsl bash -c "true" 2>$null
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

Write-Host "Анализ занятого места в WSL..." -ForegroundColor Cyan
Write-Host "Top-N каталогов: $Top, анализируемый путь: $Path" -ForegroundColor Gray
if ($NoDocker) { Write-Host "Docker-анализ отключён (-NoDocker)" -ForegroundColor Gray }

if (-not (Test-WslAvailable)) {
    Write-Host "WSL недоступен. Проверьте: wsl --status" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# 1. Реальный размер виртуального диска WSL на хосте Windows
# ---------------------------------------------------------------------------
Write-Section "1. Виртуальные диски WSL (ext4.vhdx) на хосте Windows"

# 1.1. Точный путь к диску каждого WSL-дистрибутива берём из реестра (BasePath).
$vhdxFiles = @()
$registryRoot = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss"
if (Test-Path $registryRoot) {
    Get-ChildItem $registryRoot -ErrorAction SilentlyContinue | ForEach-Object {
        $distroName = (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).DistributionName
        $basePath = (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).BasePath
        if ($basePath) {
            # BasePath — это путь вида \\?\C:\Users\...\LocalState\  (или /mnt/... для WSLC)
            $cleanPath = $basePath -replace '^\\\\\?\\', '' -replace '^\\\\', ''
            $candidate = Join-Path $cleanPath "ext4.vhdx"
            if (Test-Path $candidate) {
                $item = Get-Item $candidate -ErrorAction SilentlyContinue
                if ($item) {
                    Write-Host ("[{0}] {1}" -f $distroName, $item.FullName) -ForegroundColor Green
                    $vhdxFiles += $item
                }
            }
        }
    }
}

# 1.2. Дополнительно ищем .vhdx Docker Desktop и прочие в стандартных местах
$searchRoots = @(
    "$env:LOCALAPPDATA\Packages",
    "$env:LOCALAPPDATA\Docker",
    "$env:LOCALAPPDATA\wsl",
    "$env:APPDATA\Docker"
) | Where-Object { Test-Path $_ }

foreach ($root in $searchRoots) {
    $vf = Get-ChildItem -Path $root -Recurse -Filter "*.vhdx" -ErrorAction SilentlyContinue
    foreach ($item in $vf) {
        if ($vhdxFiles.FullName -notcontains $item.FullName) {
            Write-Host ("[disk] {0}" -f $item.FullName) -ForegroundColor Green
            $vhdxFiles += $item
        }
    }
}

if ($vhdxFiles) {
    $totalVhdx = 0
    foreach ($f in $vhdxFiles | Sort-Object Length -Descending) {
        $sizeGb = [math]::Round($f.Length / 1GB, 2)
        $totalVhdx += $f.Length
        Write-Host ("{0,8} GB  {1}" -f $sizeGb, $f.FullName) -ForegroundColor Yellow
    }
    Write-Host ("{0,8} GB  ИТОГО по найденным .vhdx" -f [math]::Round($totalVhdx / 1GB, 2)) -ForegroundColor Green
    Write-Host "Подсказка: это реальный объём на диске Windows. Внутри WSL может использоваться меньше —" -ForegroundColor Gray
    Write-Host "  освободить реальное место можно через: wsl --manage <distro> --set-sparse true" -ForegroundColor Gray
} else {
    Write-Host "Файлы .vhdx не найдены (возможно, WSL1 или нестандартное расположение)." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 2. Использование файловой системы внутри WSL
# ---------------------------------------------------------------------------
Write-Section "2. Использование ФС внутри WSL (df -h)"
wsl bash -c "df -h -x tmpfs -x devtmpfs -x squashfs"

# ---------------------------------------------------------------------------
# 3. Самые большие каталоги внутри WSL
# ---------------------------------------------------------------------------
Write-Section "3. Топ-$Top каталогов внутри WSL ($Path)"
Write-Host "Сканирование $Path ... (может занять время, ошибки доступа подавлены)" -ForegroundColor Gray
# -x: не переходить на другие ФС (исключает /mnt/c, /mnt/d и т.п.);
# 2>/dev/null — скрыть permission denied
wsl bash -c "du -x -h --max-depth=2 `"$Path`" 2>/dev/null | sort -rh | head -n $Top"

# ---------------------------------------------------------------------------
# 4. Самые большие файлы внутри WSL
# ---------------------------------------------------------------------------
Write-Section "4. Топ-$Top самых больших файлов внутри WSL ($Path)"
# ВАЖНО: PowerShell при передаче строк в нативный процесс добавляет CRLF,
# из-за чего bash видит "10\r". Поэтому пишем bash-скрипт во временный файл
# с LF-переводами строк через .NET [IO.File]::WriteAllText и запускаем его
# внутри WSL как файл (конвертируя путь через /mnt/<буква диска>/...).
# du -a -m: размер в МБ (число) + путь; sort -rn сортирует по числу.
# Цикл оставляет только файлы ([ -f ]) и печатает человекочитаемый размер.
$bashLines = @(
    "du -x -a -m '$Path' 2>/dev/null | sort -rn | while IFS=`$'\t' read -r mb item; do",
    '    if [ -f "$item" ]; then',
    '        if [ "$mb" -ge 1024 ]; then',
    '            printf ''%8.2f GB  %s\n'' "$(awk "BEGIN{printf \"%.2f\", $mb/1024}")" "$item"',
    '        else',
    '            printf ''%8d MB  %s\n'' "$mb" "$item"',
    '        fi',
    '    fi',
    "done | head -n $Top"
)
$bashScript = ($bashLines -join "`n") + "`n"

$tmpScript = Join-Path $env:TEMP "wsl_disk_usage_topfiles.sh"
# WriteAllText с LF-переводами строк; ASCII/UTF8 без BOM, чтобы bash не сбился
[System.IO.File]::WriteAllText($tmpScript, $bashScript, (New-Object System.Text.UTF8Encoding($false)))

# Конвертация Windows-пути (C:\Users\...) в WSL-путь (/mnt/c/Users/...)
$driveLetter = $tmpScript.Substring(0, 1).ToLower()
$restPath = $tmpScript.Substring(2) -replace '\\', '/'
$wslScriptPath = "/mnt/$driveLetter$restPath"

wsl bash "$wslScriptPath"
Remove-Item $tmpScript -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# 5. Анализ Docker
# ---------------------------------------------------------------------------
if (-not $NoDocker) {
    Write-Section "5. Docker: общее использование (docker system df)"
    wsl bash -c "docker system df -v 2>/dev/null || echo 'Docker недоступен внутри WSL'"
} else {
    Write-Section "5. Docker пропущен (-NoDocker)"
}

# ---------------------------------------------------------------------------
# 6. Итоговые подсказки по очистке
# ---------------------------------------------------------------------------
Write-Section "6. Подсказки по освобождению места"
Write-Host "  - Docker: безопасная очистка -> .\scripts\cleanup_docker_wsl.ps1" -ForegroundColor Gray
Write-Host "  - Docker: полная очистка     -> .\scripts\cleanup_docker_wsl.ps1 -Full" -ForegroundColor Gray
Write-Host "  - Основные «пожиратели» места в WSL/Ubuntu:" -ForegroundColor Gray
Write-Host "      * /var/lib/docker        — образы, контейнеры, volumes Docker" -ForegroundColor Gray
Write-Host "      * /var/lib/postgresql    — данные PostgreSQL" -ForegroundColor Gray
Write-Host "      * /root/.cache, ~/.cache — кеши пакетов и инструментов" -ForegroundColor Gray
Write-Host "      * /var/log               — системные и сервисные логи" -ForegroundColor Gray

Write-Host "`nГотово." -ForegroundColor Magenta
