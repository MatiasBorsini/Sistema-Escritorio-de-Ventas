param(
    [Parameter(Mandatory=$true)][string]$Instalador,
    [Parameter(Mandatory=$true)][string]$UrlHttps,
    [Parameter(Mandatory=$true)][string]$PublicadoUtc,
    [Parameter(Mandatory=$true)][string]$VersionMinima,
    [Parameter(Mandatory=$true)][string]$Notas,
    [Parameter(Mandatory=$true)][string]$ClavePrivadaFueraDelRepo,
    [Parameter(Mandatory=$true)][string]$Salida
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$clave = (Resolve-Path -LiteralPath $ClavePrivadaFueraDelRepo).Path
$raizCompleta = [IO.Path]::GetFullPath($raiz).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($clave.StartsWith($raizCompleta, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'La clave privada debe permanecer fuera del repositorio.'
}
[xml]$proyecto = Get-Content -LiteralPath (Join-Path $raiz 'src\LocalInfantil.Wpf\LocalInfantil.Wpf.csproj')
$version = $proyecto.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No se encontró Version en el proyecto WPF.' }
dotnet run --project (Join-Path $PSScriptRoot 'LocalInfantil.ManifestSigner\LocalInfantil.ManifestSigner.csproj') --configuration Release -- (Resolve-Path -LiteralPath $Instalador).Path $version $UrlHttps $PublicadoUtc $VersionMinima $Notas $clave ([IO.Path]::GetFullPath($Salida))
if ($LASTEXITCODE -ne 0) { throw "El firmador terminó con código $LASTEXITCODE." }
