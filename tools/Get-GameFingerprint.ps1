param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$file = Get-Item -LiteralPath $resolvedPath

function Get-PeArchitecture([string]$ExePath) {
    $stream = [System.IO.File]::Open($ExePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { return 'not-pe' }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { return 'not-pe' }
        $machine = $reader.ReadUInt16()
        switch ($machine) {
            0x014C { return 'x86' }
            0x8664 { return 'x64' }
            0xAA64 { return 'arm64' }
            default { return ('unknown-0x{0:X4}' -f $machine) }
        }
    }
    finally {
        if ($reader) { $reader.Dispose() }
        else { $stream.Dispose() }
    }
}

[PSCustomObject]@{
    FileName = $file.Name
    FullPath = $file.FullName
    Length = $file.Length
    Architecture = Get-PeArchitecture $resolvedPath
    SHA256 = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash
}
