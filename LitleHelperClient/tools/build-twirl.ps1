$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assets=Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
$sprite=[Drawing.Bitmap]::new((Join-Path $assets 'idle_1.png'))
try {
    for($frame=0;$frame -lt 8;$frame++) {
        $bitmap=[Drawing.Bitmap]::new(48,48);$graphics=[Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
            $graphics.TranslateTransform(24,24);$graphics.RotateTransform($frame*45)
            $graphics.DrawImage($sprite,[Drawing.Rectangle]::new(-20,-20,40,40))
            $bitmap.Save((Join-Path $assets ('twirl_'+($frame+1)+'.png')),[Drawing.Imaging.ImageFormat]::Png)
        } finally {$graphics.Dispose();$bitmap.Dispose()}
    }
} finally {$sprite.Dispose()}
