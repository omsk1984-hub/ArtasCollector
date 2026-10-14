#Requires -Version 5.0
<#
.SYNOPSIS
    Управление стеком мониторинга Artas2026 (Loki + Promtail + Prometheus + OTel)
    Docker запущен в WSL

.DESCRIPTION
    PowerShell скрипт для управления Docker контейнерами мониторинга
    через WSL subprocess.

.EXAMPLE
    .\scripts\manage_monitoring.ps1 -Action start
    .\scripts\manage_monitoring.ps1 -Action status
    .\scripts\manage_monitoring.ps1 -Action logs -Service loki
#>

param(
    [ValidateSet("start", "stop", "restart", "status", "logs", "cleanup")]
    [string]$Action = "start",
    [string]$Service = "",
    [switch]$Verbose
)

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$PROJECT_ROOT = Join-Path $SCRIPT_DIR ".."
$WSL_PROJECT_PATH = "/mnt/d/work/Artas2026"
$WSL_SCRIPT = "$WSL_PROJECT_ROOT/scripts/manage_monitoring.sh"

function Write-Info { param($Msg) Write-Host "[MONITORING] $Msg" -ForegroundColor Cyan }
function Write-Ok  { param($Msg) Write-Host "[OK] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "[WARN] $Msg" -ForegroundColor Yellow }
function Write-Err { param($Msg) Write-Host "[ERROR] $Msg" -ForegroundColor Red }

# Проверка WSL
function Test-WSL {
    try {
        $result = wsl --version 2>&1
        return $null -ne $result
    } catch {
        return $false
    }
}

# Выполнение команды в WSL
function Invoke-WSLCommand {
    param($Command)
    
    $escapedCommand = $Command -replace "'", "''"
    $fullCommand = "wsl bash -c '$escapedCommand'"
    
    if ($Verbose) {
        Write-Info "Executing: $fullCommand"
    }
    
    $output = Invoke-Expression $fullCommand 2>&1
    return $output
}

# Проверка готовности сервисов из Windows
function Check-Services-Ready {
    $maxAttempts = 10
    $attempt = 0
    
    Write-Info "Waiting for services to be ready..."
    
    while ($attempt -lt $maxAttempts) {
        $attempt++
        Start-Sleep -Seconds 3
        
        $lokiStatus = $false
        
        try {
            $lokiResp = Invoke-WebRequest -Uri "http://localhost:3100/-/healthy" -UseBasicParsing -TimeoutSec 2 2>$null
            if ($lokiResp) { $lokiStatus = $true }
        } catch { $lokiStatus = $false }
        
        if ($lokiStatus) {
            Write-Ok "All services are ready!"
            return $true
        }
        
        Write-Info "Waiting... ($attempt/$maxAttempts)"
    }
    
    Write-Warn "Some services may not be ready yet"
    return $false
}

Switch ($Action) {
    "start" {
        if (-not (Test-WSL)) {
            Write-Err "WSL is not installed or not available"
            Write-Info "Install WSL: wsl --install"
            exit 1
        }
        
        Write-Info "Starting monitoring stack via WSL..."
        
        # Запуск через bash скрипт в WSL
        $output = Invoke-WSLCommand "cd $WSL_PROJECT_ROOT && chmod +x scripts/manage_monitoring.sh && ./scripts/manage_monitoring.sh start"
        Write-Host $output
        
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "Monitoring stack started"
            Start-Sleep -Seconds 5
            Check-Services-Ready
        } else {
            Write-Err "Failed to start monitoring stack"
        }
    }
    
    "stop" {
        Write-Info "Stopping monitoring stack via WSL..."
        $output = Invoke-WSLCommand "cd $WSL_PROJECT_ROOT && ./scripts/manage_monitoring.sh stop"
        Write-Host $output
        Write-Ok "Monitoring stack stopped"
    }
    
    "restart" {
        Write-Info "Restarting monitoring stack via WSL..."
        $output = Invoke-WSLCommand "cd $WSL_PROJECT_ROOT && ./scripts/manage_monitoring.sh restart"
        Write-Host $output
        Write-Ok "Monitoring stack restarted"
    }
    
    "status" {
        Write-Info "Checking monitoring stack status via WSL..."
        $output = Invoke-WSLCommand "cd $WSL_PROJECT_ROOT && ./scripts/manage_monitoring.sh status"
        Write-Host $output
        
        # Дополнительная проверка из Windows
        echo ""
        Write-Info "Windows connectivity check:"
        
        try {
            $lokiResp = Invoke-WebRequest -Uri "http://localhost:3100/-/healthy" -UseBasicParsing -TimeoutSec 2
            Write-Ok "Loki: $($lokiResp.Content.Trim())"
        } catch {
            Write-Err "Loki: Not accessible from Windows"
        }
    }
    
    "logs" {
        Write-Info "Showing logs via WSL (Ctrl+C to stop)..."
        
        if ($Service) {
            $output = Invoke-WSLCommand "cd $WSL_PROJECT_ROOT && ./scripts/manage_monitoring.sh logs $Service"
        } else {
            $output = Invoke-WSLCommand "cd $WSL_PROJECT_ROOT && ./scripts/manage_monitoring.sh logs"
        }
        Write-Host $output
    }
    
    "cleanup" {
        Write-Warning "This will delete ALL monitoring data including logs and dashboards!"
        $confirm = Read-Host "Type 'YES' to confirm deletion"
        
        if ($confirm -eq "YES") {
            $output = Invoke-WSLCommand "cd $WSL_PROJECT_ROOT && echo YES | ./scripts/manage_monitoring.sh cleanup"
            Write-Host $output
            Write-Ok "All monitoring data deleted"
        } else {
            Write-Info "Cleanup cancelled by user"
        }
    }
}
