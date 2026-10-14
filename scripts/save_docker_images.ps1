# Save all Docker images to D:\work\DockerImages
# Usage: .\save_docker_images.ps1

$DEST_DIR = "D:\work\DockerImages"

function Write-Section($msg) {
    Write-Host ("`n=== " + $msg + " ===") -ForegroundColor Cyan
}

Write-Host "Saving Docker images via WSL..." -ForegroundColor Cyan

# Create directory if not exists
if (-not (Test-Path $DEST_DIR)) {
    Write-Host ("Creating directory: " + $DEST_DIR) -ForegroundColor Yellow
    New-Item -Path $DEST_DIR -ItemType Directory -Force | Out-Null
}

Write-Section "1. Getting image list"
$IMAGES = wsl bash -c 'docker images --format "{{.Repository}}:{{.Tag}}" 2>/dev/null' 2>$null
if ([string]::IsNullOrEmpty($IMAGES)) {
    Write-Host "ERROR: Cannot get Docker images from WSL" -ForegroundColor Red
    Write-Host "Make sure WSL is running and Docker is configured" -ForegroundColor Red
    exit 1
}
$IMG_ARRAY = ($IMAGES -split "`n").Where{$_}
Write-Host ("Found images: " + $IMG_ARRAY.Count) -ForegroundColor Green

Write-Section "2. Saving each image to tar archive"
$TOTAL_SIZE = 0
$SUCCESS_COUNT = 0
$FAIL_COUNT = 0

foreach ($IMG in $IMG_ARRAY) {
    # Replace / and : with _ for file names
    $SAFE_NAME = $IMG -replace '[/:]', '_'
    $TAR_FILE = (Join-Path $DEST_DIR ($SAFE_NAME + ".tar"))
    
    Write-Host ("Saving: " + $IMG + " -> " + $SAFE_NAME + ".tar") -ForegroundColor Gray
    
    try {
        # Save image using Docker inside WSL, output to /mnt/d/ path
        $WSL_PATH = "/mnt/d/work/DockerImages/" + $SAFE_NAME + ".tar"
        wsl bash -c ("docker save '" + $IMG + "' -o '" + $WSL_PATH + "' 2>&1")
        
        if (Test-Path $TAR_FILE) {
            $SIZE = (Get-Item $TAR_FILE).Length
            $TOTAL_SIZE += $SIZE
            $SUCCESS_COUNT++
            Write-Host ("  Saved: " + [math]::Round($SIZE/1MB, 2) + " MB") -ForegroundColor Green
        } else {
            Write-Host "  ERROR: file not created" -ForegroundColor Red
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
Write-Host ("Total size: " + [math]::Round($TOTAL_SIZE/1GB, 2) + " GB") -ForegroundColor White
Write-Host ("Directory: " + $DEST_DIR) -ForegroundColor White

Write-Host "`nTo restore images use: .\load_docker_images.ps1" -ForegroundColor Yellow
Write-Host "`nDone!" -ForegroundColor Green
