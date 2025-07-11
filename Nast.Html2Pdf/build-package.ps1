# Script para construir y empaquetar Nast.Html2Pdf para NuGet
# Este script debe ejecutarse desde el directorio del proyecto

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$Push = $false,
    [string]$ApiKey = "",
    [string]$Source = "https://api.nuget.org/v3/index.json"
)

Write-Host "=== Construcción y Empaquetado de Nast.Html2Pdf ===" -ForegroundColor Green
Write-Host "Versión: $Version" -ForegroundColor Yellow
Write-Host "Configuración: $Configuration" -ForegroundColor Yellow

# Limpiar construcciones anteriores
Write-Host "`n1. Limpiando construcciones anteriores..." -ForegroundColor Cyan
if (Test-Path "bin") { Remove-Item -Path "bin" -Recurse -Force }
if (Test-Path "obj") { Remove-Item -Path "obj" -Recurse -Force }

# Restaurar paquetes NuGet
Write-Host "`n2. Restaurando paquetes NuGet..." -ForegroundColor Cyan
dotnet restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al restaurar paquetes NuGet" -ForegroundColor Red
    exit 1
}

# Construir el proyecto
Write-Host "`n3. Construyendo el proyecto..." -ForegroundColor Cyan
dotnet build -c $Configuration --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al construir el proyecto" -ForegroundColor Red
    exit 1
}

# Ejecutar pruebas (si existen)
Write-Host "`n4. Ejecutando pruebas..." -ForegroundColor Cyan
$testProjects = Get-ChildItem -Path ".." -Filter "*.Test*.csproj" -Recurse
if ($testProjects.Count -gt 0) {
    dotnet test -c $Configuration --no-build --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Las pruebas han fallado" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "No se encontraron proyectos de prueba" -ForegroundColor Yellow
}

# Actualizar versión en el archivo del proyecto
Write-Host "`n5. Actualizando versión en el archivo del proyecto..." -ForegroundColor Cyan
$csprojPath = "Nast.Html2Pdf.csproj"
$csprojContent = Get-Content $csprojPath -Raw
$csprojContent = $csprojContent -replace '<Version>.*?</Version>', "<Version>$Version</Version>"
Set-Content -Path $csprojPath -Value $csprojContent

# Crear el paquete NuGet
Write-Host "`n6. Creando paquete NuGet..." -ForegroundColor Cyan
dotnet pack -c $Configuration --no-build --output ./nupkg

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al crear el paquete NuGet" -ForegroundColor Red
    exit 1
}

# Mostrar información del paquete creado
Write-Host "`n7. Información del paquete creado:" -ForegroundColor Cyan
$packagePath = Get-ChildItem -Path "./nupkg" -Filter "*.nupkg" | Select-Object -First 1
if ($packagePath) {
    Write-Host "   Archivo: $($packagePath.FullName)" -ForegroundColor Green
    Write-Host "   Tamaño: $([math]::Round($packagePath.Length / 1MB, 2)) MB" -ForegroundColor Green
}

# Publicar el paquete (opcional)
if ($Push) {
    Write-Host "`n8. Publicando paquete en NuGet..." -ForegroundColor Cyan
    
    if (-not $ApiKey) {
        Write-Host "Se requiere la API Key para publicar el paquete" -ForegroundColor Red
        Write-Host "Usa: -ApiKey 'tu_api_key_aqui'" -ForegroundColor Yellow
        exit 1
    }
    
    $nupkgFile = Get-ChildItem -Path "./nupkg" -Filter "*.nupkg" | Select-Object -First 1
    if ($nupkgFile) {
        dotnet nuget push $nupkgFile.FullName --api-key $ApiKey --source $Source
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Paquete publicado exitosamente!" -ForegroundColor Green
        } else {
            Write-Host "Error al publicar el paquete" -ForegroundColor Red
            exit 1
        }
    }
}

Write-Host "`n=== Proceso completado exitosamente ===" -ForegroundColor Green
Write-Host "Para publicar manualmente:" -ForegroundColor Yellow
Write-Host "dotnet nuget push ./nupkg/*.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json" -ForegroundColor Yellow
