Add-Type -AssemblyName System.Drawing
$srcPath = "D:\Application\Csharp\ArkaSoft.Notepad\Src\ArkaSoft.Notepad.UI\Assets\Icons\appLogo.png"
$outPath = "D:\Application\Csharp\ArkaSoft.Notepad\Src\ArkaSoft.Notepad.UI\Assets\Icons\appLogo.ico"

$src = [System.Drawing.Image]::FromFile($srcPath)
$sizes = @(16, 24, 32, 48, 64, 128, 256)

$sizesList = New-Object System.Collections.Generic.List[int]
$dataList = New-Object System.Collections.Generic.List[byte[]]

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $s, $s)))
    $g.Dispose()
    $png = New-Object System.IO.MemoryStream
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $sizesList.Add($s)
    $dataList.Add($png.ToArray())
    $png.Dispose()
}
$src.Dispose()

$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)                       # reserved
$bw.Write([UInt16]1)                       # type: icon
$bw.Write([UInt16]$sizesList.Count)        # image count
$offset = 6 + 16 * $sizesList.Count
for ($i = 0; $i -lt $sizesList.Count; $i++) {
    $s = $sizesList[$i]
    $data = $dataList[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256
    $bw.Write([Byte]$dim)                  # width
    $bw.Write([Byte]$dim)                  # height
    $bw.Write([Byte]0)                     # palette
    $bw.Write([Byte]0)                     # reserved
    $bw.Write([UInt16]1)                   # planes
    $bw.Write([UInt16]32)                  # bpp
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
for ($i = 0; $i -lt $sizesList.Count; $i++) {
    $bw.Write($dataList[$i])
}
$bw.Flush()
$bw.Close()
Write-Host "ICO written: $((Get-Item $outPath).Length) bytes"
