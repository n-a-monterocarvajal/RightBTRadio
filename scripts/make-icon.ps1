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

$markColor = [System.Drawing.Color]::FromArgb(255, 15, 108, 189)

function New-MarkPng([int]$Size) {
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $centerX = $Size * 0.30
        $centerY = $Size * 0.72
        $dotRadius = $Size * 0.13

        $brush = New-Object System.Drawing.SolidBrush($markColor)
        $graphics.FillEllipse(
            $brush,
            [single]($centerX - $dotRadius),
            [single]($centerY - $dotRadius),
            [single]($dotRadius * 2),
            [single]($dotRadius * 2))
        $brush.Dispose()

        # Two heavier arcs below 32 px, three lighter ones above.
        $radii = if ($Size -lt 32) { @(0.32, 0.56) } else { @(0.30, 0.46, 0.62) }
        $strokeWidth = if ($Size -lt 32) { $Size * 0.13 } else { $Size * 0.10 }

        $pen = New-Object System.Drawing.Pen($markColor, [single]$strokeWidth)
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
                [single](-78),
                [single](72))
        }

        $pen.Dispose()
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
