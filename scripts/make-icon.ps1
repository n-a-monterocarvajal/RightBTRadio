<#
.SYNOPSIS
    Generates assets/RightBTRadio.ico from code.

.DESCRIPTION
    The mark is a beacon: a filled dot with signal arcs radiating from it, standing for
    the one radio the application keeps on the air. It is drawn here rather than shipped
    as a binary asset so the icon is reproducible, reviewable in a diff, and provably
    ours - the icon RightKeyboard uses arrived with its pre-fork import and carries a
    different licence.

    Small sizes drop to two arcs and a heavier stroke: three thin arcs smear at 16 px.

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

Add-Type -AssemblyName System.Drawing

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot 'assets/RightBTRadio.ico'
}

# Azul de la marca y el mismo verde que el indicador de conexión de la ventana.
$beaconColor = [System.Drawing.Color]::FromArgb(255, 15, 108, 189)
$checkColor = [System.Drawing.Color]::FromArgb(255, 16, 124, 65)

function New-MarkPng([int]$Size) {
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $small = $Size -lt 32

        # Faro: el radio que la aplicación deja emitiendo.
        $centerX = $Size * 0.20
        $centerY = $Size * 0.74
        $dotRadius = $Size * 0.105

        $brush = New-Object System.Drawing.SolidBrush($beaconColor)
        $graphics.FillEllipse(
            $brush,
            [single]($centerX - $dotRadius),
            [single]($centerY - $dotRadius),
            [single]($dotRadius * 2),
            [single]($dotRadius * 2))
        $brush.Dispose()

        # Dos arcos más gruesos por debajo de 32 px, tres más finos por encima.
        $radii = if ($small) { @(0.26, 0.46) } else { @(0.24, 0.38, 0.52) }
        $beaconStroke = if ($small) { $Size * 0.115 } else { $Size * 0.088 }

        $pen = New-Object System.Drawing.Pen($beaconColor, [single]$beaconStroke)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        foreach ($factor in $radii) {
            $radius = $Size * $factor
            $graphics.DrawArc(
                $pen,
                [single]($centerX - $radius),
                [single]($centerY - $radius),
                [single]($radius * 2),
                [single]($radius * 2),
                [single](-80),
                [single](74))
        }

        $pen.Dispose()

        # Distintivo de conformidad, abajo a la derecha: la prioridad quedó resuelta.
        # Por debajo de 24 px no se dibuja: el tic blanco dentro del círculo se emborrona
        # y ensucia el faro, así que el icono se simplifica en vez de empeorar.
        if ($Size -ge 24)
        {
        $badgeRadius = $Size * 0.26
        $badgeX = $Size * 0.72
        $badgeY = $Size * 0.72

        $badgeBrush = New-Object System.Drawing.SolidBrush($checkColor)
        $graphics.FillEllipse(
            $badgeBrush,
            [single]($badgeX - $badgeRadius),
            [single]($badgeY - $badgeRadius),
            [single]($badgeRadius * 2),
            [single]($badgeRadius * 2))
        $badgeBrush.Dispose()

        $checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]($Size * 0.075))
        $checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $checkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawLines($checkPen, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]($badgeX - $badgeRadius * 0.48), [single]$badgeY)),
            (New-Object System.Drawing.PointF([single]($badgeX - $badgeRadius * 0.10), [single]($badgeY + $badgeRadius * 0.38))),
            (New-Object System.Drawing.PointF([single]($badgeX + $badgeRadius * 0.50), [single]($badgeY - $badgeRadius * 0.42)))))
        $checkPen.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    # Unary comma: without it PowerShell unrolls the byte array into the pipeline and the
    # caller gets an Object[] of bytes, which BinaryWriter.Write has no overload for.
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

    # ICONDIRENTRY, 16 bytes each; the payloads follow the whole directory.
    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $writer.Write([byte]($image.Size % 256))   # 256 is stored as 0
        $writer.Write([byte]($image.Size % 256))
        $writer.Write([byte]0)                     # palette entries
        $writer.Write([byte]0)                     # reserved
        $writer.Write([uint16]1)                   # planes
        $writer.Write([uint16]32)                  # bits per pixel
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
