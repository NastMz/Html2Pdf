# Script para ejecutar tests de Nast.Html2Pdf
param(
    [string]$Configuration = "Debug",
    [string]$TestFilter = "",
    [string[]]$Frameworks = @("net8.0", "net9.0"),
    [switch]$Coverage,
    [switch]$Verbose
)

Write-Host "🧪 Ejecutando tests de Nast.Html2Pdf..." -ForegroundColor Green
Write-Host "Configuración: $Configuration" -ForegroundColor Yellow
Write-Host "Frameworks: $($Frameworks -join ', ')" -ForegroundColor Yellow
Write-Host "Provisioning: PuppeteerSharp-compatible browser setup via on-demand download or explicit executable path" -ForegroundColor Yellow

$testProject = "Tests\Nast.Html2Pdf.Tests.csproj"
try {
    foreach ($framework in $Frameworks) {
        $testArgs = @(
            "test", $testProject
            "--configuration", $Configuration
            "--framework", $framework
            "--logger", "console;verbosity=normal"
            "--logger", "trx;LogFileName=TestResults-$framework.trx"
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

        Write-Host "Ejecutando comando: dotnet $($testArgs -join ' ')" -ForegroundColor Cyan
        & dotnet $testArgs

        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Algunos tests fallaron en $framework. Código de salida: $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }

    Write-Host "✅ Todos los tests pasaron exitosamente!" -ForegroundColor Green
    
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
