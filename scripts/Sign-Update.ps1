param(
    [switch]$Initialize,
    [string]$ZipPath
)

$ErrorActionPreference = 'Stop'
if ([bool]$Initialize -eq [bool]$ZipPath) {
    throw 'Use -Initialize to set up the key, or -ZipPath to sign a Windows release ZIP.'
}

$root = Split-Path -Parent $PSScriptRoot
$publicPath = Join-Path $root 'Assets\UpdatePublicKey.xml'
$keyParameters = [System.Security.Cryptography.CspParameters]::new(24)
$keyParameters.KeyContainerName = 'WarrenStack.Tricera360.UpdateSigning'
if (-not $Initialize -or (Test-Path -LiteralPath $publicPath)) {
    $keyParameters.Flags = [System.Security.Cryptography.CspProviderFlags]::UseExistingKey
} else {
    $keyParameters.Flags = [System.Security.Cryptography.CspProviderFlags]::UseNonExportableKey
}

$key = [System.Security.Cryptography.RSACryptoServiceProvider]::new(3072, $keyParameters)
try {
    $key.PersistKeyInCsp = $true
    $publicKey = $key.ToXmlString($false)
    if (Test-Path -LiteralPath $publicPath) {
        if ((Get-Content -LiteralPath $publicPath -Raw).Trim() -ne $publicKey) {
            throw 'The signing key on this account does not match the public key in the repo.'
        }
    } elseif ($Initialize) {
        [IO.File]::WriteAllText($publicPath, $publicKey)
    } else {
        throw 'The public update key is missing. Restore it from the repo before signing.'
    }

    if ($Initialize) {
        Write-Host 'Public update key ready at Assets/UpdatePublicKey.xml.'
        return
    }

    $zip = Get-Item -LiteralPath $ZipPath
    if ($zip.Name -cnotmatch '^Tricera360-v([0-9]+\.[0-9]+\.[0-9]+)-win-x64\.zip$') {
        throw 'Use a ZIP named Tricera360-v<VERSION>-win-x64.zip.'
    }
    $version = $Matches[1]
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ("Tricera360-sign-" + [guid]::NewGuid().ToString('N'))
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'Tricera360.exe' })
        if ($entries.Count -ne 1 -or $entries[0].Length -le 0 -or $entries[0].Length -gt 160MB) {
            throw 'The ZIP must contain one root Tricera360.exe within the updater size limit.'
        }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $scratch)
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($scratch)
        if ($info.FileVersion -ne "$version.0") {
            throw "The executable file version must match $version."
        }
        $manifest = [ordered]@{
            Version = $version
            Sha256 = (Get-FileHash -LiteralPath $scratch -Algorithm SHA256).Hash
            Size = $entries[0].Length
        }
        $bytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress))
        $signature = $key.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        [IO.File]::WriteAllBytes((Join-Path $zip.DirectoryName 'Tricera360-update.json'), $bytes)
        [IO.File]::WriteAllBytes((Join-Path $zip.DirectoryName 'Tricera360-update.sig'), $signature)
        Write-Host 'Signed update files written next to the ZIP.'
    }
    finally {
        $archive.Dispose()
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch }
    }
}
finally {
    $key.Dispose()
}
