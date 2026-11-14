# Build-Installer.ps1 — Genera el instalador productivo F10.
#
# Flujo (se detiene ante el primer fallo, sin compilar el instalador):
#   1. Lee la versión de una única fuente: <Version> del csproj WPF.
#   2. Limpieza segura de artifacts\publish\net10-f10-win-x64: solo esa carpeta,
#      solo si no contiene datos personales; nada fuera de ella se toca.
#   3. Publicación Release self-contained .NET 10 (win-x64) recién generada.
#      Si la publicación falla, no se compila el instalador.
#   4. Verificación de que la publicación no contiene archivos privados.
#   5. Compilación del instalador con Inno Setup 6 (ISCC.exe), pasando
#      /DAppVersion con la versión del csproj.
#
# El instalador generado queda en artifacts\installer\ (fuera de git).
# No instala ni desinstala nada: esos pasos son manuales y están descritos
# en docs/PRUEBAS_MANUALES.md. No toca ninguna base existente.
#
# Uso desde la raíz del repositorio:
#   powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1

$ErrorActionPreference = "Stop"

$Raiz = Split-Path -Parent $PSScriptRoot
$Publicacion = Join-Path $Raiz "artifacts\publish\net10-f10-win-x64"
$ProyectoWpf = Join-Path $Raiz "src\LocalInfantil.Wpf\LocalInfantil.Wpf.csproj"
$ScriptIss = Join-Path $PSScriptRoot "inno\LocalInfantil.iss"
$Salida = Join-Path $Raiz "artifacts\installer"
$Iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

$nombresPersonales = @("secrets.json", "appsettings.Local.json", "credentials.json", "client_secrets.json", "token.json", "tokens.json")
$extensionesPersonales = @(".db", ".db-shm", ".db-wal", ".db3", ".sqlite", ".sqlite-shm", ".sqlite-wal", ".sqlite3", ".libackup", ".dpapi", ".log", ".bak", ".pfx", ".p12", ".pem", ".key", ".token")

function Buscar-ArchivosPersonales {
    param([string]$Carpeta)
    if (-not (Test-Path -LiteralPath $Carpeta)) { return @() }
    Get-ChildItem -LiteralPath $Carpeta -Recurse -File -ErrorAction Stop | Where-Object {
        ($extensionesPersonales -contains $_.Extension.ToLowerInvariant()) -or ($nombresPersonales -contains $_.Name)
    }
}

# Versión desde una única fuente: <Version> del csproj (F10-T01).
[xml]$csproj = Get-Content -LiteralPath $ProyectoWpf
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "El csproj no define <Version>. No se compiló el instalador."
}
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versión con formato inesperado ('$version'). Se espera MAJOR.MINOR.PATCH."
}
$Instalador = Join-Path $Salida "LocalInfantil-Setup-$version-net10-win-x64.exe"
Write-Host "Versión (única fuente, csproj): $version"

if (-not (Test-Path -LiteralPath $Iscc)) {
    throw "No se encontró ISCC.exe en '$Iscc'. Instalá Inno Setup 6 antes de continuar."
}
if (-not (Test-Path -LiteralPath $ScriptIss)) {
    throw "No se encontró el script Inno en '$ScriptIss'."
}

# 1. Limpieza estrictamente limitada a la carpeta de publicación generada.
$pubFull = [System.IO.Path]::GetFullPath($Publicacion)
$raizFull = [System.IO.Path]::GetFullPath($Raiz)
$relativa = $pubFull.Substring($raizFull.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
$esperada = Join-Path "artifacts" (Join-Path "publish" "net10-f10-win-x64")
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

# 4. Compilar el instalador con la versión del csproj.
if (-not (Test-Path -LiteralPath $Salida)) {
    New-Item -ItemType Directory -Path $Salida | Out-Null
}
& $Iscc $ScriptIss "/DAppVersion=$version"
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe terminó con código $LASTEXITCODE."
}

# 5. Informar el resultado.
$info = Get-Item -LiteralPath $Instalador
Write-Host ("Instalador generado: " + $info.FullName)
Write-Host ("Tamaño (bytes): " + $info.Length)
