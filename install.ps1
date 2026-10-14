# Foxify installer for Windows.
#
#   irm https://raw.githubusercontent.com/kikutano/foxify/main/install.ps1 | iex
#
# Environment variables:
#   FOXIFY_VERSION      version to install, e.g. 0.1.0 (default: latest release)
#   FOXIFY_INSTALL_DIR  target directory (default: %LOCALAPPDATA%\Programs\foxify)
$ErrorActionPreference = 'Stop'

$repo = 'kikutano/foxify'
$installDir = if ($env:FOXIFY_INSTALL_DIR) { $env:FOXIFY_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\foxify' }

# win-x64 also runs on Windows on ARM through x64 emulation.
$archive = 'foxify-win-x64.zip'

$baseUrl = if ($env:FOXIFY_VERSION) {
    "https://github.com/$repo/releases/download/v$($env:FOXIFY_VERSION.TrimStart('v'))"
} else {
    "https://github.com/$repo/releases/latest/download"
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid())
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    Write-Host "Downloading $baseUrl/$archive"
    Invoke-WebRequest "$baseUrl/$archive" -OutFile (Join-Path $tmp $archive) -UseBasicParsing

    try {
        Invoke-WebRequest "$baseUrl/SHA256SUMS" -OutFile (Join-Path $tmp 'SHA256SUMS') -UseBasicParsing
        $line = Get-Content (Join-Path $tmp 'SHA256SUMS') | Where-Object { $_ -match " $([regex]::Escape($archive))$" }
        $expected = ($line -split ' ')[0]
        $actual = (Get-FileHash (Join-Path $tmp $archive) -Algorithm SHA256).Hash
        if ($expected -and ($expected -ne $actual)) { throw "Checksum mismatch for $archive." }
    } catch [System.Net.WebException], [Microsoft.PowerShell.Commands.HttpResponseException] {
        Write-Host 'SHA256SUMS not found, skipping checksum verification.'
    }

    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    Expand-Archive (Join-Path $tmp $archive) -DestinationPath $installDir -Force
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $installDir) {
    [Environment]::SetEnvironmentVariable('Path', "$userPath;$installDir".TrimStart(';'), 'User')
    $env:Path = "$env:Path;$installDir"
    Write-Host "Added $installDir to your user PATH (restart the terminal to pick it up)."
}

Write-Host "Installed $(& (Join-Path $installDir 'foxify.exe') --version) to $installDir"
Write-Host 'Update later with: foxify update'
