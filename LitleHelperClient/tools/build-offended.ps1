$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assets=Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
for($frame=1;$frame -le 2;$frame++) {
    $bitmap=[Drawing.Bitmap]::new((Join-Path $assets "sad_$frame.png"))
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $screen=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(21,58,82))
    $glow=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(123,238,241))
    try {
        $graphics.FillRectangle($screen,14,11,20,14)
        # Inward brows, lowered eyes, a small frown; second frame blinks.
        $graphics.FillRectangle($glow,16,13,3,1);$graphics.FillRectangle($glow,19,14,2,1)
        $graphics.FillRectangle($glow,27,14,2,1);$graphics.FillRectangle($glow,29,13,3,1)
        $height=if($frame-eq 1){3}else{1}
        $graphics.FillRectangle($glow,18,17,2,$height);$graphics.FillRectangle($glow,28,17,2,$height)
        $graphics.FillRectangle($glow,22,22,4,1);$graphics.FillRectangle($glow,21,23,1,1);$graphics.FillRectangle($glow,26,23,1,1)
        $bitmap.Save((Join-Path $assets "offended_$frame.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally {$graphics.Dispose();$screen.Dispose();$glow.Dispose();$bitmap.Dispose()}
}
