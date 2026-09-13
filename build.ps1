$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspaceDir = Split-Path -Parent (Split-Path -Parent $projectDir)
$portableDir = Join-Path $projectDir 'vendor\OpenSpeedy'
$openSpeedyLicense = Join-Path $portableDir 'LICENSE'
if (-not (Test-Path -LiteralPath (Join-Path $portableDir 'bridge32.exe'))) {
    $portableDir = Join-Path $workspaceDir 'work\OpenSpeedy-portable-build'
}
if (-not (Test-Path -LiteralPath $openSpeedyLicense)) {
    $openSpeedyLicense = Join-Path $workspaceDir 'work\OpenSpeedy\LICENSE'
}
$buildDir = Join-Path $projectDir 'build'
$packageDir = Join-Path $buildDir '东方变速器'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $packageDir 'src') -Force | Out-Null

& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:"$projectDir\app.manifest" /win32icon:"$projectDir\assets\logo.ico" /out:"$packageDir\东方变速器.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "$projectDir\TouhouSpeedController.cs"
if ($LASTEXITCODE -ne 0) { throw "C# compiler failed with exit code $LASTEXITCODE" }

Copy-Item -LiteralPath "$portableDir\bridge32.exe","$portableDir\bridge64.exe","$portableDir\speedpatch32.dll","$portableDir\speedpatch64.dll" -Destination $packageDir -Force
Copy-Item -LiteralPath "$projectDir\README.txt","$projectDir\OPEN_SOURCE_NOTICE.txt" -Destination $packageDir -Force
Copy-Item -LiteralPath "$projectDir\assets\logo.png" -Destination $packageDir -Force
Copy-Item -LiteralPath "$projectDir\TouhouSpeedController.cs","$projectDir\app.manifest","$projectDir\build.ps1" -Destination (Join-Path $packageDir 'src') -Force
Copy-Item -LiteralPath "$projectDir\assets\logo.png","$projectDir\assets\logo.ico" -Destination (Join-Path $packageDir 'src') -Force
Copy-Item -LiteralPath $openSpeedyLicense -Destination (Join-Path $packageDir 'LICENSE-GPLv3.txt') -Force

Write-Host "Built package: $packageDir"
