$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assets=Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
$angles=@(-8,0,8,0,8,0,-8,0)
for($frame=1;$frame -le 8;$frame++) {
 $source=[Drawing.Bitmap]::new((Join-Path $assets ('celebrate_'+(1+(($frame-1)%2))+'.png')))
 $bitmap=[Drawing.Bitmap]::new(48,48,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
 $graphics=[Drawing.Graphics]::FromImage($bitmap)
 try {
  $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
  $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
  $graphics.TranslateTransform(24,24)
  $graphics.RotateTransform($angles[$frame-1])
  if($frame -gt 4){$graphics.ScaleTransform(-1,1)}
  $hop=if($frame%2 -eq 0){-2}else{0}
  $graphics.DrawImage($source,-24,-24+$hop,48,48)
  $bitmap.Save((Join-Path $assets "dance_$frame.png"),[Drawing.Imaging.ImageFormat]::Png)
 } finally {$graphics.Dispose();$bitmap.Dispose();$source.Dispose()}
}
