# =============================================================================
# generate-icon.ps1 — 生成「时迹」应用图标
#
# 产物：
#   src/AppUsageTracker/Assets/app.ico   多尺寸图标（exe / 窗口 / 托盘）
#   src/AppUsageTracker/Assets/app.png   256px 位图（界面内展示）
#
# 图形：蓝色渐变圆角方块 + 居中白色圆盘 + 从 12 点顺时针的深蓝扇形，
#       表达「已计量的时间占比」，在 16px 下仍可辨识。
#
# 用法：sh manager.sh icon   或   powershell -File tools/generate-icon.ps1
# =============================================================================
[CmdletBinding()]
param(
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $projectRoot = Split-Path -Parent $PSScriptRoot
    $OutputDirectory = Join-Path $projectRoot 'src/AppUsageTracker/Assets'
}

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# 图标尺寸：覆盖任务栏、资源管理器、托盘和高 DPI 场景
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-IconBitmap {
    param([int] $Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $inset = $Size * 0.03
        $side = $Size - (2 * $inset)
        $radius = $Size * 0.22

        # 圆角方块路径
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $diameter = $radius * 2
        $path.AddArc($inset, $inset, $diameter, $diameter, 180, 90)
        $path.AddArc($inset + $side - $diameter, $inset, $diameter, $diameter, 270, 90)
        $path.AddArc($inset + $side - $diameter, $inset + $side - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc($inset, $inset + $side - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()

        $gradientRect = New-Object System.Drawing.RectangleF($inset, $inset, $side, $side)
        $backgroundBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $gradientRect,
            [System.Drawing.ColorTranslator]::FromHtml('#4682F0'),
            [System.Drawing.ColorTranslator]::FromHtml('#1B49A8'),
            [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        $graphics.FillPath($backgroundBrush, $path)
        $backgroundBrush.Dispose()
        $path.Dispose()

        # 居中白色圆盘
        $discSize = $Size * 0.60
        $discOffset = ($Size - $discSize) / 2
        $discBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $graphics.FillEllipse($discBrush, $discOffset, $discOffset, $discSize, $discSize)
        $discBrush.Dispose()

        # 从 12 点顺时针的深蓝扇形（-90 度为 12 点方向）
        $wedgeBrush = New-Object System.Drawing.SolidBrush(
            [System.Drawing.ColorTranslator]::FromHtml('#1B49A8'))
        $graphics.FillPie($wedgeBrush, $discOffset, $discOffset, $discSize, $discSize, -90, 105)
        $wedgeBrush.Dispose()
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Get-DibBytes {
    param([System.Drawing.Bitmap] $Bitmap)

    # ICO 内的图像必须写成 DIB（BITMAPINFOHEADER + 自下而上的 BGRA + AND 掩码）。
    # 不能使用 PNG 压缩：Roslyn 生成 Win32 资源时会按 DIB 头解析，遇到 PNG 会直接报
    # CS7065「Unable to read beyond the end of the stream」。
    $width = $Bitmap.Width
    $height = $Bitmap.Height

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $maskRowBytes = [int][Math]::Floor((($width + 31) / 32)) * 4
        $xorSize = $width * $height * 4
        $andSize = $maskRowBytes * $height

        # BITMAPINFOHEADER：高度写两倍，涵盖 XOR 像素区和 AND 掩码区
        $writer.Write([uint32] 40)
        $writer.Write([int32] $width)
        $writer.Write([int32] ($height * 2))
        $writer.Write([uint16] 1)
        $writer.Write([uint16] 32)
        $writer.Write([uint32] 0)                      # BI_RGB
        $writer.Write([uint32] ($xorSize + $andSize))
        $writer.Write([int32] 0)
        $writer.Write([int32] 0)
        $writer.Write([uint32] 0)
        $writer.Write([uint32] 0)

        # 锁定位图一次性读出 BGRA，避免逐像素 GetPixel 的开销
        $rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
        $data = $Bitmap.LockBits(
            $rect,
            [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $rowBytes = $width * 4
            $buffer = New-Object byte[] ($rowBytes)
            # DIB 自下而上存储
            for ($y = $height - 1; $y -ge 0; $y--) {
                $rowPointer = [System.IntPtr]::Add($data.Scan0, $y * $data.Stride)
                [System.Runtime.InteropServices.Marshal]::Copy($rowPointer, $buffer, 0, $rowBytes)
                $writer.Write($buffer)
            }
        }
        finally {
            $Bitmap.UnlockBits($data)
        }

        # AND 掩码：32bpp 已带 alpha，掩码全 0 表示不透明
        $writer.Write((New-Object byte[] ($andSize)))
        $writer.Flush()
        # 前置逗号包一层数组，避免 PowerShell 在返回时把 byte[] 展开成 object[]，
        # 那会让 BinaryWriter.Write 误匹配单字节重载，每张图只写出 1 个字节。
        return ,$stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

# 逐尺寸渲染并转成 DIB 负载
$payloads = New-Object 'System.Collections.Generic.List[object]'
$maximum = ($sizes | Measure-Object -Maximum).Maximum
$largest = $null
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size
    $payloads.Add([pscustomobject]@{ Size = $size; Bytes = (Get-DibBytes -Bitmap $bitmap) })
    if ($size -eq $maximum) {
        $largest = $bitmap
    }
    else {
        $bitmap.Dispose()
    }
}

# 写出 ICO：ICONDIR + N * ICONDIRENTRY + DIB 负载
$icoPath = Join-Path $OutputDirectory 'app.ico'
$icoStream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($icoStream)
try {
    $writer.Write([uint16] 0)                 # reserved
    $writer.Write([uint16] 1)                 # type = icon
    $writer.Write([uint16] $payloads.Count)   # image count

    $offset = 6 + (16 * $payloads.Count)
    foreach ($payload in $payloads) {
        $dimension = if ($payload.Size -ge 256) { 0 } else { $payload.Size }
        $writer.Write([byte] $dimension)      # width
        $writer.Write([byte] $dimension)      # height
        $writer.Write([byte] 0)               # palette count
        $writer.Write([byte] 0)               # reserved
        $writer.Write([uint16] 1)             # color planes
        $writer.Write([uint16] 32)            # bits per pixel
        $writer.Write([uint32] $payload.Bytes.Length)
        $writer.Write([uint32] $offset)
        $offset += $payload.Bytes.Length
    }

    foreach ($payload in $payloads) {
        $writer.Write([byte[]] $payload.Bytes)
    }
}
finally {
    $writer.Dispose()
    $icoStream.Dispose()
}

# 写出 256px PNG，供界面内 Image 控件使用
$pngPath = Join-Path $OutputDirectory 'app.png'
$largest.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$largest.Dispose()

Write-Output ("已生成 {0}（{1} 个尺寸）" -f $icoPath, $payloads.Count)
Write-Output ("已生成 {0}" -f $pngPath)
