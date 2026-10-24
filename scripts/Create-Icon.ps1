$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing
$bitmap=New-Object Drawing.Bitmap 256,256
$graphics=[Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint=[Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$brushes=@{}
foreach($item in @{'background'='#141C29';'frame'='#AFC9DE';'door'='#263E54';'accent'='#FF9838';'white'='#F1F6FA';'line'='#67869E'}.GetEnumerator()){$brushes[$item.Key]=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($item.Value))}
$font=[Drawing.Font]::new('Arial',23,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
$caption=[Drawing.Font]::new('Arial',24,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
$format=[Drawing.StringFormat]::new()
$format.Alignment=[Drawing.StringAlignment]::Center
try{
 $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#141C29'))
 $graphics.DrawString('LC OFFICE',$font,$brushes.white,[Drawing.RectangleF]::new(0,14,256,32),$format)
 $graphics.FillRectangle($brushes.frame,39,55,144,139)
 $graphics.FillRectangle($brushes.background,47,63,128,123)
 $graphics.FillRectangle($brushes.door,53,69,55,111)
 $graphics.FillRectangle($brushes.door,113,69,55,111)
 $graphics.FillRectangle($brushes.line,107,69,3,111)
 $graphics.FillRectangle($brushes.frame,48,182,125,4)
 $up=[Drawing.Point[]]@([Drawing.Point]::new(204,77),[Drawing.Point]::new(192,95),[Drawing.Point]::new(216,95))
 $down=[Drawing.Point[]]@([Drawing.Point]::new(204,133),[Drawing.Point]::new(192,115),[Drawing.Point]::new(216,115))
 $graphics.FillPolygon($brushes.frame,$up)
 $graphics.FillPolygon($brushes.frame,$down)
 $pen=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#FF9838'),8)
 try{$graphics.DrawLine($pen,222,140,184,179)}finally{$pen.Dispose()}
 $head=[Drawing.Point[]]@([Drawing.Point]::new(176,168),[Drawing.Point]::new(197,187),[Drawing.Point]::new(183,203),[Drawing.Point]::new(160,184))
 $graphics.FillPolygon($brushes.accent,$head)
 $graphics.DrawString('CLEANUP',$caption,$brushes.accent,[Drawing.RectangleF]::new(0,214,256,34),$format)
 $bitmap.Save((Join-Path $repo 'package\icon.png'),[Drawing.Imaging.ImageFormat]::Png)
}finally{
 $graphics.Dispose();$bitmap.Dispose();$font.Dispose();$caption.Dispose();$format.Dispose()
 foreach($brush in $brushes.Values){$brush.Dispose()}
}
Write-Host 'Created package/icon.png (256 x 256).'
