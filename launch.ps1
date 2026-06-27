# CoolSleep Launch Script
# Starts all three services: Python thermal service, .NET API, and Blazor Web client

$projectRoot = $PSScriptRoot

Write-Host "Starting CoolSleep services..." -ForegroundColor Cyan
Write-Host ""

# Get pwsh full path
$pwshPath = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if (-not $pwshPath) {
    $pwshPath = "powershell.exe"
}

# 1. Python Thermal Service (port 8000)
Write-Host "Starting Python thermal service (port 8000)..." -ForegroundColor Green
$pythonCmd = "cd '$projectRoot\python'; if (Test-Path '.\coolsleep_venv\Scripts\Activate.ps1') { & '.\coolsleep_venv\Scripts\Activate.ps1' } else { python -m venv coolsleep_venv; & '.\coolsleep_venv\Scripts\Activate.ps1'; pip install -r requirements.txt }; uvicorn thermal.api:app --reload --port 8000"
Start-Process $pwshPath -ArgumentList "-NoExit", "-Command", $pythonCmd

Start-Sleep -Seconds 2

# 2. .NET Core API (port 5000)
Write-Host "Starting ASP.NET Core API (port 5000)..." -ForegroundColor Green
$apiCmd = "cd '$projectRoot\src\CoolSleep.Api'; dotnet run"
Start-Process $pwshPath -ArgumentList "-NoExit", "-Command", $apiCmd

Start-Sleep -Seconds 2

# 3. Blazor Web Client (port 7000)
Write-Host "Starting Blazor Web client (port 7000)..." -ForegroundColor Green
$webCmd = "cd '$projectRoot\src\CoolSleep.Web'; dotnet run"
Start-Process $pwshPath -ArgumentList "-NoExit", "-Command", $webCmd

Write-Host ""
Write-Host "All services launching!" -ForegroundColor Green
Write-Host ""
Write-Host "Services:" -ForegroundColor Cyan
Write-Host "   Python Thermal: http://localhost:8000/docs" -ForegroundColor White
Write-Host "   .NET API:       http://localhost:5000/swagger" -ForegroundColor White
Write-Host "   Web Client:     https://localhost:7000" -ForegroundColor White
Write-Host ""
Write-Host "Tip: Close any window to stop that service." -ForegroundColor Yellow
