[CmdletBinding()]
param(
    [string]$Version = '1.0.1'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'KeyGlowKeeper.cs'
$icon = Join-Path $root 'assets\icon.ico'
$dist = Join-Path $root 'dist'
$packageName = "KeyGlowKeeper-v$Version"
$package = Join-Path $dist $packageName
$exe = Join-Path $dist 'KeyGlowKeeper.exe'
$zip = Join-Path $dist "$packageName.zip"
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$distFull = [IO.Path]::GetFullPath($dist).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$packageFull = [IO.Path]::GetFullPath($package)

if (-not $packageFull.StartsWith($distFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The package path must remain inside the dist directory.'
}

if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'The .NET Framework C# compiler was not found.'
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path $package) { Remove-Item -LiteralPath $package -Recurse -Force }
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ "/out:$exe" "/win32icon:$icon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }

New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item -LiteralPath $exe,(Join-Path $root 'Install.cmd'),(Join-Path $root 'Uninstall.cmd'),(Join-Path $root 'LICENSE') -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $package 'README.md')
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -CompressionLevel Optimal

$hashLines = @(
    "$(Get-FileHash -Algorithm SHA256 -LiteralPath $exe | Select-Object -ExpandProperty Hash)  KeyGlowKeeper.exe",
    "$(Get-FileHash -Algorithm SHA256 -LiteralPath $zip | Select-Object -ExpandProperty Hash)  $packageName.zip"
)
$hashLines | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii

Get-Item $exe,$zip,(Join-Path $dist 'SHA256SUMS.txt') | Select-Object Name,Length,LastWriteTime
