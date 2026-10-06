$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$iconOutput=Join-Path $PSScriptRoot '../src/AIInvestmentWorkbench.App/Assets/Workbench.ico'
$images=@()
foreach($size in @(16,32,48,256)) {
 $bitmap=[Drawing.Bitmap]::new($size,$size)
 $graphics=[Drawing.Graphics]::FromImage($bitmap)
 $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#14685F'))
 $pen=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#FFFFFF'),[single]($size*.075))
 $points=[Drawing.PointF[]]@([Drawing.PointF]::new($size*.20,$size*.73),[Drawing.PointF]::new($size*.39,$size*.48),[Drawing.PointF]::new($size*.55,$size*.57),[Drawing.PointF]::new($size*.81,$size*.25))
 $graphics.DrawLines($pen,$points)
 $graphics.DrawLine($pen,[single]($size*.58),[single]($size*.25),[single]($size*.81),[single]($size*.25))
 $graphics.DrawLine($pen,[single]($size*.81),[single]($size*.25),[single]($size*.81),[single]($size*.48))
 $stream=[IO.MemoryStream]::new(); $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
 $images+=,@{Size=$size;Bytes=$stream.ToArray()}
 $stream.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file=[IO.File]::Create($iconOutput); $writer=[IO.BinaryWriter]::new($file)
try {
 $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
 $offset=6+16*$images.Count
 foreach($entry in $images) { $dimension=if($entry.Size -eq 256){0}else{$entry.Size}; $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset); $offset+=$entry.Bytes.Length }
 foreach($entry in $images) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
