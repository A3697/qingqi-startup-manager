$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
foreach ($kind in @('Main','Worker')) {
    $frames = @()
    foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
        $canvas = [Drawing.Bitmap]::new(256,256)
        $g = [Drawing.Graphics]::FromImage($canvas); $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([Drawing.Color]::Transparent)
        $path = [Drawing.Drawing2D.GraphicsPath]::new()
        $path.AddArc(8,8,72,72,180,90); $path.AddArc(176,8,72,72,270,90)
        $path.AddArc(176,176,72,72,0,90); $path.AddArc(8,176,72,72,90,90); $path.CloseFigure()
        $color = if ($kind -eq 'Main') { [Drawing.Color]::FromArgb(47,91,219) } else { [Drawing.Color]::FromArgb(13,139,135) }
        $brush = [Drawing.SolidBrush]::new($color); $g.FillPath($brush,$path)
        $pen = [Drawing.Pen]::new([Drawing.Color]::White,18); $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        if ($kind -eq 'Main') {
            $g.DrawArc($pen,52,52,152,152,35,285)
            $arrow = [Drawing.PointF[]]@([Drawing.PointF]::new(107,86),[Drawing.PointF]::new(164,128),[Drawing.PointF]::new(107,170))
            $g.FillPolygon([Drawing.Brushes]::White,$arrow)
        } else {
            $g.DrawRectangle($pen,51,62,154,119); $g.DrawLine($pen,55,96,201,96); $g.DrawLine($pen,93,148,162,148)
            $g.FillEllipse([Drawing.Brushes]::White,178,168,45,45)
            $dot = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,187,62)); $g.FillEllipse($dot,186,176,29,29); $dot.Dispose()
        }
        $g.Dispose(); $brush.Dispose(); $path.Dispose(); $pen.Dispose()
        $small = [Drawing.Bitmap]::new($size,$size); $sg = [Drawing.Graphics]::FromImage($small)
        $sg.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $sg.DrawImage($canvas,0,0,$size,$size); $sg.Dispose(); $canvas.Dispose()
        $stream = [IO.MemoryStream]::new(); $small.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
        if ($size -eq 256) { $small.Save((Join-Path $assets ($kind + '.png')),[Drawing.Imaging.ImageFormat]::Png) }
        $frames += ,@{Size=$size;Data=$stream.ToArray()}; $stream.Dispose(); $small.Dispose()
    }
    $writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assets ($kind + '.ico'))))
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }; $writer.Dispose()
}
