param([string]$Vectors="tests/fixtures/generated/pixel-vectors.json")
$ErrorActionPreference="Stop"
Add-Type -AssemblyName PresentationCore
$cases=Get-Content $Vectors -Raw | ConvertFrom-Json
$failed=@()
foreach($case in $cases){
    $format=[System.Windows.Media.PixelFormats].GetProperty($case.format).GetValue($null,$null)
    $palette=$null
    if($null -ne $case.palette){
        $colors=New-Object 'System.Collections.Generic.List[System.Windows.Media.Color]'
        foreach($color in $case.palette){$colors.Add([System.Windows.Media.Color]::FromArgb($color.a,$color.r,$color.g,$color.b))}
        $palette=[System.Windows.Media.Imaging.BitmapPalette]::new($colors)
    }
    $data=[Convert]::FromBase64String($case.pixels)
    $bitmap=[System.Windows.Media.Imaging.BitmapSource]::Create($case.width,$case.height,96,96,$format,$palette,[System.Array]$data,$case.stride)
    $converted=[System.Windows.Media.Imaging.FormatConvertedBitmap]::new()
    $converted.BeginInit();$converted.Source=$bitmap;$converted.DestinationFormat=[System.Windows.Media.PixelFormats]::Bgra32;$converted.EndInit()
    $output=New-Object byte[] ($case.width*$case.height*4)
    $converted.CopyPixels($output,$case.width*4,0)
    if([Convert]::ToBase64String($output) -cne $case.bgra32){
        $failed+=$case.name
        Write-Output "MISMATCH $($case.name): $([BitConverter]::ToString($output))"
    }else{Write-Output "PASS $($case.name)"}
}
# Compare the independent Windows reference for every possible Gray16 input.
$bounds=(Get-Content "tests/fixtures/generated/gray16-quantization.json" -Raw | ConvertFrom-Json).lowerBounds
$ramp=New-Object byte[] 131072
for($value=0;$value -lt 65536;$value++){$ramp[2*$value]=$value -band 255;$ramp[2*$value+1]=$value -shr 8}
$bitmap=[System.Windows.Media.Imaging.BitmapSource]::Create(256,256,96,96,[System.Windows.Media.PixelFormats]::Gray16,$null,[System.Array]$ramp,512)
$converted=[System.Windows.Media.Imaging.FormatConvertedBitmap]::new($bitmap,[System.Windows.Media.PixelFormats]::Bgra32,$null,0)
$output=New-Object byte[] 262144
$converted.CopyPixels($output,1024,0)
$level=0;$differences=0
for($value=0;$value -lt 65536;$value++){
    while($level -lt $bounds.Count -and $value -ge $bounds[$level]){$level++}
    $offset=4*$value
    if($output[$offset] -ne $level -or $output[$offset+1] -ne $level -or $output[$offset+2] -ne $level -or $output[$offset+3] -ne 255){
        if($differences -lt 12){Write-Output "Gray16 difference at $value : expected $level, actual $($output[$offset])"}
        $differences++
    }
}
if($differences){$failed+='Gray16 exhaustive';Write-Output "Gray16 exhaustive differences: $differences"}
else{Write-Output "PASS all 65536 Gray16 values"}
foreach($case in @(
    @{ right=@(255,128,128,128); expected=@(0,229,229,229) },
    @{ right=@(128,0,64,255); expected=@(127,255,249,0) }
)) {
    $right=[System.Windows.Media.Color]::FromArgb($case.right[0],$case.right[1],$case.right[2],$case.right[3])
    $actual=[System.Windows.Media.Color]::Subtract([System.Windows.Media.Colors]::White,$right)
    if((@($actual.A,$actual.R,$actual.G,$actual.B) -join ',') -cne ($case.expected -join ',')) {
        $failed+='linear color subtraction'
    }
}
foreach($kind in @('rgb','rgba','indexed')) {
    $input=[IO.File]::OpenRead("tests/fixtures/generated/colors-$kind.png")
    try {
        $decoder=[System.Windows.Media.Imaging.PngBitmapDecoder]::new($input,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::None,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame=$decoder.Frames[0]
    } finally {$input.Dispose()}
    $expectedFormats=switch($kind){rgb {'Bgr32'};rgba {'Bgra32'};indexed {'Indexed8'}}
    Write-Output "PNG $kind pixel format: $($frame.Format)"
    if(@($expectedFormats) -cnotcontains $frame.Format.ToString()){$failed+="PNG $kind color type"}
    $converted=[System.Windows.Media.Imaging.FormatConvertedBitmap]::new($frame,
        [System.Windows.Media.PixelFormats]::Bgra32,$null,0)
    $pixels=New-Object byte[] 8
    $converted.CopyPixels($pixels,8,0)
    $alpha=if($kind -eq 'rgb'){255}else{128}
    $expected=[byte[]]@(0,0,255,$alpha,0,255,0,255)
    if([Convert]::ToBase64String($pixels) -cne [Convert]::ToBase64String($expected)) {
        $failed+="PNG $kind pixels"
    }
    Write-Output "PASS PNG $kind color type and pixels"
}
$background=[System.Windows.Media.Imaging.BitmapSource]::Create(1,1,96,96,
    [System.Windows.Media.PixelFormats]::Bgra32,$null,[byte[]]@(80,60,40,128),4)
$foreground=[System.Windows.Media.Imaging.BitmapSource]::Create(1,1,96,96,
    [System.Windows.Media.PixelFormats]::Bgra32,$null,[byte[]]@(50,100,200,128),4)
$visual=New-Object System.Windows.Media.DrawingVisual
$context=$visual.RenderOpen()
try {
    $context.DrawImage($background,[System.Windows.Rect]::new(0,0,1,1))
    $context.DrawImage($foreground,[System.Windows.Rect]::new(0,0,1,1))
} finally {$context.Dispose()}
$rendered=[System.Windows.Media.Imaging.RenderTargetBitmap]::new(1,1,96,96,
    [System.Windows.Media.PixelFormats]::Pbgra32)
$rendered.Render($visual)
$composed=New-Object byte[] 4
$rendered.CopyPixels($composed,4,0)
Write-Output "WPF source-over premultiplied pixels: $($composed -join ',')"
if(($composed -join ',') -cne '45,65,110,192'){$failed+='source-over composition'}
if($failed.Count){throw "WPF pixel differences: $($failed -join ', ')"}
