$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
$sprite = [Drawing.Bitmap]::new((Join-Path $assets 'idle_1.png'))
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16,24,32,48,64,128,256)
try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size,$size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $shape = [Drawing.Drawing2D.GraphicsPath]::new()
        $radius = $size * .22; $extent = $size - 1
        $shape.AddArc(0,0,$radius,$radius,180,90); $shape.AddArc($extent-$radius,0,$radius,$radius,270,90)
        $shape.AddArc($extent-$radius,$extent-$radius,$radius,$radius,0,90); $shape.AddArc(0,$extent-$radius,$radius,$radius,90,90); $shape.CloseFigure()
        $fill = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0,0,$size,$size),[Drawing.Color]::FromArgb(99,102,241),[Drawing.Color]::FromArgb(139,92,246),45)
        try {
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.FillPath($fill,$shape)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
            # Crop transparent sprite padding; small sizes focus on the robot's face.
            $crop = if($size -le 32){[Drawing.Rectangle]::new(10,6,28,29)}else{[Drawing.Rectangle]::new(9,6,30,39)}
            $height=[int]($size*.82); $width=[int]($height*$crop.Width/$crop.Height)
            $target=[Drawing.Rectangle]::new([int](($size-$width)/2),[int](($size-$height)/2),$width,$height)
            $graphics.DrawImage($sprite,$target,$crop,[Drawing.GraphicsUnit]::Pixel)
            $stream=[IO.MemoryStream]::new(); $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png); $frames.Add($stream.ToArray()); $stream.Dispose()
            if($size -eq 256){$bitmap.Save((Join-Path $assets 'app-icon.png'),[Drawing.Imaging.ImageFormat]::Png)}
        } finally {$graphics.Dispose();$fill.Dispose();$shape.Dispose();$bitmap.Dispose()}
    }
    $stream=[IO.File]::Create((Join-Path $assets 'PixelHelper.ico'));$writer=[IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count);$offset=6+16*$sizes.Count
        for($i=0;$i -lt $sizes.Count;$i++){$dimension=if($sizes[$i]-eq 256){0}else{$sizes[$i]};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset);$offset+=$frames[$i].Length}
        foreach($frame in $frames){$writer.Write($frame)}
    } finally {$writer.Dispose();$stream.Dispose()}
} finally {$sprite.Dispose()}
