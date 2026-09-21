# Build-PrototypeInstaller.ps1 — Genera el instalador de prueba F00-T06.
#
# Flujo (se detiene ante el primer fallo, sin compilar el instalador):
#   1. Limpieza segura de artifacts\publish\net10-win-x64: solo esa carpeta,
#      solo si no contiene datos personales; nada fuera de ella se toca.
#   2. Publicación Release self-contained .NET 10 (win-x64) recién generada.
#      Si la publicación falla, no se compila el instalador.
#   3. Verificación de que la publicación no contiene archivos privados.
#   4. Compilación del instalador con Inno Setup 6 (ISCC.exe).
#
# El instalador generado queda en artifacts\installer\ (fuera de git).
# No instala ni desinstala nada: esos pasos son manuales y están descritos
# en docs/PRUEBAS_MANUALES.md. No toca la base provisional existente.
#
# Uso desde la raíz del repositorio:
#   powershell -ExecutionPolicy Bypass -File .\installer\Build-PrototypeInstaller.ps1

$ErrorActionPreference = "Stop"

$Raiz = Split-Path -Parent $PSScriptRoot
$Publicacion = Join-Path $Raiz "artifacts\publish\net10-win-x64"
$ProyectoWpf = Join-Path $Raiz "src\LocalInfantil.Wpf\LocalInfantil.Wpf.csproj"
$ScriptIss = Join-Path $PSScriptRoot "inno\LocalInfantil-Prototype.iss"
$Salida = Join-Path $Raiz "artifacts\installer"
$Instalador = Join-Path $Salida "LocalInfantil-Prototype-Setup-net10-win-x64.exe"
$Iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

$nombresPersonales = @("secrets.json", "appsettings.Local.json")
$extensionesPersonales = @(".db", ".db-shm", ".db-wal", ".sqlite", ".sqlite-shm", ".sqlite-wal", ".log", ".bak", ".pfx", ".p12", ".pem", ".key", ".token")

function Buscar-ArchivosPersonales {
    param([string]$Carpeta)
    if (-not (Test-Path -LiteralPath $Carpeta)) { return @() }
    Get-ChildItem -LiteralPath $Carpeta -Recurse -File -ErrorAction Stop | Where-Object {
        ($extensionesPersonales -contains $_.Extension.ToLowerInvariant()) -or ($nombresPersonales -contains $_.Name)
    }
}

if (-not (Test-Path -LiteralPath $Iscc)) {
    throw "No se encontró ISCC.exe en '$Iscc'. Instalá Inno Setup 6 antes de continuar."
}
if (-not (Test-Path -LiteralPath $ScriptIss)) {
    throw "No se encontró el script Inno en '$ScriptIss'."
}
if (-not (Test-Path -LiteralPath $ProyectoWpf)) {
    throw "No se encontró el proyecto WPF en '$ProyectoWpf'."
}

# 1. Limpieza estrictamente limitada a la carpeta de publicación generada.
#    Guarda: la ruta debe ser exactamente artifacts\publish\net10-win-x64 bajo
#    la raíz del repositorio; cualquier otra ruta aborta sin borrar nada.
$pubFull = [System.IO.Path]::GetFullPath($Publicacion)
$raizFull = [System.IO.Path]::GetFullPath($Raiz)
$relativa = $pubFull.Substring($raizFull.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
$esperada = Join-Path "artifacts" (Join-Path "publish" "net10-win-x64")
if ($relativa -ne $esperada) {
    throw "Ruta de publicación inesperada ('$pubFull'). No se limpia nada."
}
if (Test-Path -LiteralPath $pubFull) {
    $previos = @(Buscar-ArchivosPersonales -Carpeta $pubFull)
    if ($previos.Count -gt 0) {
        $previos | ForEach-Object { Write-Host ("DATO PERSONAL DETECTADO, NO SE LIMPIA: " + $_.FullName) }
        throw "La carpeta de publicación contiene datos personales. Limpieza abortada; no se publicó ni compiló nada."
    }
    Remove-Item -Path (Join-Path $pubFull "*") -Recurse -Force
    Write-Host "Carpeta de publicación limpiada (contenía solo salidas de compilación anteriores)."
}

# 2. Publicación Release recién generada. Si falla, no se compila el instalador.
& dotnet publish $ProyectoWpf --configuration Release --runtime win-x64 --self-contained true --output $pubFull --disable-build-servers -m:1 -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish terminó con código $LASTEXITCODE. No se compiló el instalador."
}
if (-not (Test-Path -LiteralPath (Join-Path $pubFull "LocalInfantil.Wpf.exe"))) {
    throw "La publicación no produjo LocalInfantil.Wpf.exe. No se compiló el instalador."
}
Write-Host "Publicación Release generada."

# 3. La publicación recién generada no debe contener archivos privados.
$prohibidos = $extensionesPersonales + @(".pdb")
$hallazgos = @(Get-ChildItem -LiteralPath $pubFull -Recurse -File -ErrorAction Stop | Where-Object {
    ($prohibidos -contains $_.Extension.ToLowerInvariant()) -or ($nombresPersonales -contains $_.Name)
})
if ($hallazgos.Count -gt 0) {
    $hallazgos | ForEach-Object { Write-Host ("ARCHIVO PRIVADO DETECTADO: " + $_.FullName) }
    throw "La publicación contiene archivos privados. No se compiló el instalador."
}
Write-Host "Publicación verificada: sin bases, respaldos, símbolos ni secretos."

# 4. Compilar el instalador.
if (-not (Test-Path -LiteralPath $Salida)) {
    New-Item -ItemType Directory -Path $Salida | Out-Null
}
& $Iscc $ScriptIss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe terminó con código $LASTEXITCODE."
}

# 5. Informar el resultado.
$info = Get-Item -LiteralPath $Instalador
Write-Host ("Instalador generado: " + $info.FullName)
Write-Host ("Tamaño (bytes): " + $info.Length)
