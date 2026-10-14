# Load all Docker images from D:\work\DockerImages
# Usage: .\load_docker_images.ps1

$SRC_DIR = "D:\work\DockerImages"

function Write-Section($msg) {
    Write-Host ("`n=== " + $msg + " ===") -ForegroundColor Cyan
}

Write-Host "Loading Docker images via WSL..." -ForegroundColor Cyan

if (-not (Test-Path $SRC_DIR)) {
    Write-Host ("ERROR: Directory not found: " + $SRC_DIR) -ForegroundColor Red
    exit 1
}

Write-Section "1. Finding archives"
$TAR_FILES = Get-ChildItem (Join-Path $SRC_DIR "*.tar")
Write-Host ("Found archives: " + $TAR_FILES.Count) -ForegroundColor Green

Write-Section "2. Loading each image"
$SUCCESS_COUNT = 0
$FAIL_COUNT = 0

foreach ($TAR in $TAR_FILES) {
    Write-Host ("Loading: " + $TAR.Name) -ForegroundColor Gray
    
    try {
        # Load image using Docker inside WSL
        $WSL_PATH = "/mnt/d/work/DockerImages/" + $TAR.Name
        wsl bash -c ("docker load --input '" + $WSL_PATH + "' 2>&1")
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Loaded successfully" -ForegroundColor Green
            $SUCCESS_COUNT++
        } else {
            Write-Host "  ERROR loading" -ForegroundColor Red
            $FAIL_COUNT++
        }
    } catch {
        Write-Host ("  ERROR: " + $_.Exception.Message) -ForegroundColor Red
        $FAIL_COUNT++
    }
}

Write-Section "Summary:"
Write-Host ("Success: " + $SUCCESS_COUNT) -ForegroundColor Green
if ($FAIL_COUNT -gt 0) {
    Write-Host ("Errors: " + $FAIL_COUNT) -ForegroundColor Red
} else {
    Write-Host ("Errors: " + $FAIL_COUNT) -ForegroundColor Green
}

Write-Section "Check loaded images"
wsl bash -c 'docker images 2>/dev/null'

Write-Host "`nDone!" -ForegroundColor Green
