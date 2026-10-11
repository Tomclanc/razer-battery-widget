param([string]$SigningThumbprint)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
dotnet publish src\RazerBatteryWidget.csproj -c Release -o app --self-contained true
if ($LASTEXITCODE) { throw 'Build failed' }
New-Item -ItemType Directory -Force app\Assets,app\Public | Out-Null
New-Item -ItemType Directory -Force app\Assets\Devices | Out-Null
Copy-Item DeviceAssets\*.png app\Assets\Devices\
Copy-Item AppxManifest.xml app\AppxManifest.xml
Add-Type -AssemblyName System.Drawing
foreach ($size in @(44,150,256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,68,214,44),($size/18))
    $graphics.DrawRectangle($pen,($size*0.18),($size*0.3),($size*0.58),($size*0.4))
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,68,214,44))
    $graphics.FillRectangle($brush,($size*0.25),($size*0.37),($size*0.44),($size*0.26))
    $graphics.FillRectangle($brush,($size*0.8),($size*0.42),($size*0.06),($size*0.16))
    $name = if($size -eq 256){'Logo.png'}else{"Logo$size.png"}
    $bitmap.Save((Join-Path $PSScriptRoot "app\Assets\$name"))
    $graphics.Dispose(); $bitmap.Dispose(); $pen.Dispose(); $brush.Dispose()
}
$bitmap = New-Object System.Drawing.Bitmap(600,440)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::FromArgb(255,30,32,34))
$font = New-Object System.Drawing.Font('Microsoft YaHei UI',16)
$green = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,68,214,44))
$graphics.DrawString('雷蛇设备电量',$font,[System.Drawing.Brushes]::White,24,20)
$rows = @('巴塞利斯蛇 V3 专业版 35K','噬魂金蝎 V2 无线版','HYPERPOLLING 接收器 1','HYPERPOLLING 接收器 2')
for($i=0;$i -lt 4;$i++) {
    $y = 85 + 70*$i
    $graphics.DrawString($rows[$i],$font,[System.Drawing.Brushes]::White,24,$y)
    $level = if($i -lt 2){'100%'}else{'USB'}
    $graphics.DrawString($level,$font,$green,490,$y)
}
$graphics.DrawString('雷云本地数据 · 自动刷新',$font,[System.Drawing.Brushes]::Gray,24,380)
$bitmap.Save((Join-Path $PSScriptRoot 'app\Assets\Preview.png'))
$graphics.Dispose(); $bitmap.Dispose(); $font.Dispose(); $green.Dispose()
if(Test-Path (Join-Path $PSScriptRoot '外观预览.png')) { Copy-Item (Join-Path $PSScriptRoot '外观预览.png') app\Assets\Preview.png -Force }
$tool = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.7627\bin\10.0.26100.0\x64\makeappx.exe'
& $tool pack /d app /p RazerBatteryWidget.msix /o | Select-Object -Last 2
if($LASTEXITCODE) { throw 'Packaging failed' }
if ($SigningThumbprint) {
    $signTool = Join-Path (Split-Path $tool) 'signtool.exe'
    & $signTool sign /fd SHA256 /sha1 $SigningThumbprint RazerBatteryWidget.msix
    if ($LASTEXITCODE) { throw 'Signing failed' }
    Export-Certificate -Cert (Get-Item "Cert:\CurrentUser\My\$SigningThumbprint") -FilePath RazerBatteryWidget.cer | Out-Null
} else {
    Write-Host 'Unsigned MSIX created. Sign it with a matching certificate before installation.'
}
Pop-Location
