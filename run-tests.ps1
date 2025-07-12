# Script para ejecutar tests de Nast.Html2Pdf
param(
    [string]$Configuration = "Debug",
    [string]$TestFilter = "",
    [switch]$Coverage,
    [switch]$Verbose
)

Write-Host "🧪 Ejecutando tests de Nast.Html2Pdf..." -ForegroundColor Green
Write-Host "Configuración: $Configuration" -ForegroundColor Yellow

$testProject = "Tests\Nast.Html2Pdf.Tests.csproj"
$testArgs = @(
    "test", $testProject
    "--configuration", $Configuration
    "--logger", "console;verbosity=normal"
    "--logger", "trx;LogFileName=TestResults.trx"
    "--results-directory", "TestResults"
)

if ($TestFilter) {
    $testArgs += "--filter", $TestFilter
    Write-Host "Filtro de tests: $TestFilter" -ForegroundColor Yellow
}

if ($Coverage) {
    $testArgs += "--collect", "XPlat Code Coverage"
    Write-Host "Cobertura de código: Habilitada" -ForegroundColor Yellow
}

if ($Verbose) {
    $testArgs += "--verbosity", "diagnostic"
    Write-Host "Verbosidad: Detallada" -ForegroundColor Yellow
}

try {
    Write-Host "Ejecutando comando: dotnet $($testArgs -join ' ')" -ForegroundColor Cyan
    
    # Instalar Playwright browsers si es necesario
    Write-Host "🌐 Instalando browsers de Playwright..." -ForegroundColor Blue
    dotnet build $testProject --configuration $Configuration
    pwsh -f bin\$Configuration\net8.0\playwright.ps1 install
    
    # Ejecutar tests
    & dotnet $testArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Todos los tests pasaron exitosamente!" -ForegroundColor Green
    } else {
        Write-Host "❌ Algunos tests fallaron. Código de salida: $LASTEXITCODE" -ForegroundColor Red
    }
    
    # Mostrar resultados si existen
    if (Test-Path "TestResults") {
        Write-Host "📊 Resultados de tests guardados en: TestResults/" -ForegroundColor Blue
        Get-ChildItem "TestResults" -Filter "*.trx" | ForEach-Object {
            Write-Host "  - $($_.Name)" -ForegroundColor Gray
        }
    }
    
} catch {
    Write-Host "❌ Error ejecutando tests: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "🔚 Ejecución de tests completada." -ForegroundColor Green
