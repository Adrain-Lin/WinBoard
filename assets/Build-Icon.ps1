$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$source=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'WinBoard.png'))
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=@()
try {
    foreach($size in $sizes) {
        $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.CompositingQuality=[Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size))
        $buffer=[IO.MemoryStream]::new();$bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
        $frames+=,@{Size=$size;Bytes=$buffer.ToArray()}
        $buffer.Dispose();$graphics.Dispose();$bitmap.Dispose()
    }
    $file=[IO.File]::Create((Join-Path $PSScriptRoot 'WinBoard.ico'));$writer=[IO.BinaryWriter]::new($file)
    try {
        $writer.Write([UInt16]0);$writer.Write([UInt16]1);$writer.Write([UInt16]$frames.Count)
        $offset=6+16*$frames.Count
        foreach($frame in $frames) {
            $dimension=if($frame.Size -eq 256){0}else{$frame.Size}
            $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
            $writer.Write([UInt16]1);$writer.Write([UInt16]32);$writer.Write([UInt32]$frame.Bytes.Length);$writer.Write([UInt32]$offset)
            $offset+=$frame.Bytes.Length
        }
        foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
    } finally {$writer.Dispose();$file.Dispose()}
} finally {$source.Dispose()}
