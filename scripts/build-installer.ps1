<#
.SYNOPSIS
    Publishes both executables into one folder and builds the installer.

.DESCRIPTION
    Adapted from RightKeyboard's build-installer.ps1, which belongs to its MIT layer.
    The two applications publish into the same directory so the tray finds the settings
    window beside itself instead of falling back to the development tree.

    The payload is framework-dependent. RightKeyboard ships self-contained so it can
    install without elevation, since installing the runtimes machine-wide requires it.
    That premise does not survive here: enabling and disabling PnP nodes needs
    administrator anyway, so the installer elevates and downloads the two Microsoft
    runtimes when they are missing.

.PARAMETER Version
    Defaults to the version declared by the tray project.

.PARAMETER Configuration
    Release by default.

.PARAMETER IsccPath
    Path to Inno Setup's ISCC.exe. Falls back to $env:ISCC_PATH and the usual locations.

.PARAMETER SkipInstaller
    Publish and verify, then stop. For checking the publish without Inno Setup installed.

.EXAMPLE
    pwsh -File scripts/build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$IsccPath = $env:ISCC_PATH,
    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$trayProject = Join-Path $repositoryRoot 'RightBTRadio\RightBTRadio.csproj'
$settingsProject = Join-Path $repositoryRoot 'RightBTRadio.WinUI\RightBTRadio.WinUI.csproj'
$installerScript = Join-Path $repositoryRoot 'installer\RightBTRadio.iss'
$publishDirectory = Join-Path $repositoryRoot 'artifacts\publish\win-x64'
$installerDirectory = Join-Path $repositoryRoot 'artifacts\installer'

if (-not $Version) {
    [xml]$project = Get-Content -LiteralPath $trayProject
    $Version = [string]$project.Project.PropertyGroup.Version
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$') {
    throw "La versión '$Version' no tiene un formato válido."
}

foreach ($directory in @($publishDirectory, $installerDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $publishDirectory, $installerDirectory -Force | Out-Null

function Publish([string]$Project, [string[]]$ExtraProperties) {
    $arguments = @(
        'publish', $Project,
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'false',
        '--output', $publishDirectory,
        "-p:Version=$Version",
        '-p:ContinuousIntegrationBuild=true',
        '-p:DebugSymbols=false',
        '-p:DebugType=None'
    ) + $ExtraProperties

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Falló dotnet publish para $Project."
    }
}

Write-Host "Publicando RightBTRadio $Version en $publishDirectory"
Publish $trayProject @()
Publish $settingsProject @('-p:WindowsAppSDKSelfContained=false')

# El destino CopyWinUiGeneratedResourcesToPublish del proyecto WinUI los copia. Si
# alguna vez deja de hacerlo, la ventana publicada se cierra con 0xC000027B y sin esta
# comprobación el instalador saldría igual.
foreach ($resource in @('App.xbf', 'RightBTRadio.WinUI.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $resource))) {
        throw "Falta el recurso WinUI generado en la publicación: $resource"
    }
}

foreach ($executable in @('RightBTRadio.exe', 'RightBTRadio.WinUI.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $executable))) {
        throw "Falta el ejecutable en la publicación: $executable"
    }
}

# El despliegue autocontenido del Windows App SDK copia el runtime de Windows ML aunque
# el proyecto no lo use. WindowsAppSDKMLPassthroughOnnxRuntime no lo evita
# (WindowsAppSDK 5969), así que se eliminan tras publicar. RightBTRadio no referencia
# ninguna API de Windows.AI: si alguna vez lo hiciera, hay que quitar este bloque.
$machineLearningBinaries = @(
    'onnxruntime.dll',
    'DirectML.dll',
    'Microsoft.ML.OnnxRuntime.dll',
    'Microsoft.Windows.AI.MachineLearning.dll',
    'Microsoft.Windows.AI.MachineLearning.Projection.dll'
)
$removedBytes = 0
foreach ($binary in $machineLearningBinaries) {
    $path = Join-Path $publishDirectory $binary
    if (Test-Path -LiteralPath $path) {
        $removedBytes += (Get-Item -LiteralPath $path).Length
        Remove-Item -LiteralPath $path -Force
    }
}
Write-Host ('Runtime de Windows ML descartado: {0:N1} MB.' -f ($removedBytes / 1MB))

$publishedSize = (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ('Publicación lista: {0} archivos, {1:N1} MB.' -f `
    (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File).Count, ($publishedSize / 1MB))

if ($SkipInstaller) {
    return
}

$candidates = @(
    $IsccPath,
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

$compiler = $candidates | Select-Object -First 1
if (-not $compiler) {
    throw "La publicación quedó lista en '$publishDirectory', pero no se encontró ISCC.exe. Instale Inno Setup o defina ISCC_PATH."
}

& $compiler "/DPublishDir=$publishDirectory" "/DOutputDir=$installerDirectory" "/DAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw 'Falló la compilación del instalador con Inno Setup.'
}

$setupPath = Join-Path $installerDirectory "RightBTRadio-$Version-Setup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Inno Setup no generó el archivo esperado: $setupPath"
}

$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$setupPath.sha256" -Value "$hash  $(Split-Path -Leaf $setupPath)" -Encoding ASCII

Write-Host ('Instalador listo: {0} ({1:N1} MB)' -f $setupPath, ((Get-Item -LiteralPath $setupPath).Length / 1MB))
Write-Host "SHA-256: $hash"
