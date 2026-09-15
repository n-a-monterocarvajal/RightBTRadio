<#
.SYNOPSIS
    Generates assets/RightBTRadio.ico from Material Design Icons path data.

.DESCRIPTION
    The mark composes two Material Design Icons shapes the way MDI's own "-check"
    variants do, such as cookie-check: the subject, and the standard check badge in the
    bottom-right corner. Here the subject is `bluetooth` and the badge says the priority
    is resolved.

    The paths are the verbatim `d` attributes from the MDI library, drawn on its 24x24
    grid. WPF parses them: its path mini-language is a superset of SVG path data, so no
    parser of our own is needed and the shapes stay exactly as MDI drew them.

    The badge is MDI's `check-circle`, scaled down and laid over the lower-right of the
    glyph, which is carved by a slightly larger circle so the badge reads apart from it.

    Attribution: Material Design Icons by Pictogrammers, Apache License 2.0. See the
    licence section of README.md.

    The Bluetooth figure mark is a trademark of Bluetooth SIG, and MDI's Apache licence
    covers the artwork, not trademark rights. Raised and decided by the project owner.

    Small sizes drop the badge: below 24 px the check smears into the glyph.

    Entries are PNG-compressed, which Windows has supported since Vista and this
    application requires Windows 11 anyway.

.EXAMPLE
    pwsh -File scripts/make-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [int[]]$Sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot 'assets/RightBTRadio.ico'
}

# Datos de ruta de Material Design Icons, rejilla 24x24.
$bluetoothPath = 'M14.88,16.29L13,18.17V14.41M13,5.83L14.88,7.71L13,9.58M17.71,7.71L12,2H11V9.58L6.41,5L5,6.41L10.59,12L5,17.58L6.41,19L11,14.41V22H12L17.71,16.29L13.41,12L17.71,7.71Z'
# check-circle: círculo de radio 10 en (12, 12) con el visto calado.
$checkPath = 'M12 2C6.5 2 2 6.5 2 12S6.5 22 12 22 22 17.5 22 12 17.5 2 12 2M10 17L5 12L6.41 10.59L10 14.17L17.59 6.58L19 8L10 17Z'
# Centro, escala y separación de la insignia en la rejilla de 24.
$badgeCenter = 18.5
$badgeScale = 0.5
$badgeGap = 1

$bluetoothBrush = New-Object System.Windows.Media.SolidColorBrush(
    [System.Windows.Media.Color]::FromRgb(15, 108, 189))
# El mismo verde que el indicador de conexión de la ventana de ajustes.
$checkBrush = New-Object System.Windows.Media.SolidColorBrush(
    [System.Windows.Media.Color]::FromRgb(16, 124, 65))

function New-MarkPng([int]$Size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    try {
        # Todo se dibuja en la rejilla de 24 de MDI y se escala al tamaño pedido al final.
        $context.PushTransform((New-Object System.Windows.Media.ScaleTransform(
            ($Size / 24), ($Size / 24))))

        $bluetooth = [System.Windows.Media.Geometry]::Parse($bluetoothPath)
        $withBadge = $Size -ge 24

        if ($withBadge) {
            # La insignia es check-circle reducido, montado sobre el glifo. Un anillo
            # transparente, un poco mayor que el círculo, lo separa del trazo del B.
            $carve = New-Object System.Windows.Media.EllipseGeometry(
                (New-Object System.Windows.Point($badgeCenter, $badgeCenter)),
                (10 * $badgeScale + $badgeGap), (10 * $badgeScale + $badgeGap))
            $carved = [System.Windows.Media.Geometry]::Combine(
                $bluetooth, $carve, [System.Windows.Media.GeometryCombineMode]::Exclude, $null)
            $context.DrawGeometry($bluetoothBrush, $null, $carved)

            $group = New-Object System.Windows.Media.TransformGroup
            $group.Children.Add((New-Object System.Windows.Media.TranslateTransform(-12, -12)))
            $group.Children.Add((New-Object System.Windows.Media.ScaleTransform($badgeScale, $badgeScale)))
            $group.Children.Add((New-Object System.Windows.Media.TranslateTransform($badgeCenter, $badgeCenter)))
            $context.PushTransform($group)
            $context.DrawGeometry($checkBrush, $null, [System.Windows.Media.Geometry]::Parse($checkPath))
            $context.Pop()
        }
        else {
            # Sin insignia el glifo ocupa el cuadro entero, centrado.
            $group = New-Object System.Windows.Media.TransformGroup
            $group.Children.Add((New-Object System.Windows.Media.TranslateTransform(-5, -2)))
            $group.Children.Add((New-Object System.Windows.Media.ScaleTransform(1.05, 1.05)))
            $group.Children.Add((New-Object System.Windows.Media.TranslateTransform(5.3, 1)))
            $context.PushTransform($group)
            $context.DrawGeometry($bluetoothBrush, $null, $bluetooth)
            $context.Pop()
        }

        $context.Pop()
    }
    finally {
        $context.Close()
    }

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)

    # Coma unaria: sin ella PowerShell desenrolla el arreglo de bytes en la tubería y
    # quien llama recibe un Object[], para el que BinaryWriter.Write no tiene sobrecarga.
    return ,$stream.ToArray()
}

$images = foreach ($size in $Sizes) {
    [PSCustomObject]@{ Size = $size; Bytes = New-MarkPng $size }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$output = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($output)
try {
    # ICONDIR
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    # ICONDIRENTRY, 16 bytes cada una; las cargas van después de todo el directorio.
    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $writer.Write([byte]($image.Size % 256))   # 256 se guarda como 0
        $writer.Write([byte]($image.Size % 256))
        $writer.Write([byte]0)                     # entradas de paleta
        $writer.Write([byte]0)                     # reservado
        $writer.Write([uint16]1)                   # planos
        $writer.Write([uint16]32)                  # bits por píxel
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Bytes)
    }
}
finally {
    $writer.Dispose()
    $output.Dispose()
}

$sizeList = ($Sizes | ForEach-Object { "${_}px" }) -join ', '
Write-Host "Icono escrito en $OutputPath ($sizeList)."

# La barra de título de la ventana muestra el icono desde un PNG y no desde el ICO: con
# ExtendsContentIntoTitleBar el marco no dibuja el suyo, y un PNG evita depender de que el
# decodificador de imágenes de XAML elija bien el fotograma de un ICO.
$pngPath = [System.IO.Path]::ChangeExtension($OutputPath, '.png')
[System.IO.File]::WriteAllBytes($pngPath, (New-MarkPng 32))
Write-Host "PNG de 32px escrito en $pngPath."
