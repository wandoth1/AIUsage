param(
    [Parameter(Mandatory = $true)][ValidateSet('win-x64', 'win-arm64')][string]$Runtime,
    [Parameter(Mandatory = $true)][string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Self-contained runtime packs are download dependencies, not necessarily libraries in
# project.assets.json. Read the runtime versions recorded in the built executable's config.
$configPath = Join-Path $root "src/AIUsage.Windows/bin/Release/net10.0-windows/$Runtime/AIUsage.runtimeconfig.json"
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$frameworks = @($config.runtimeOptions.includedFrameworks)
$coreVersion = ($frameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' } | Select-Object -First 1).version
$desktopVersion = ($frameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' } | Select-Object -First 1).version
if ($coreVersion -notmatch '^\d+\.\d+\.\d+$' -or $desktopVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Cannot determine the exact included runtime versions from $configPath."
}
$destination = Join-Path $PublishDirectory 'runtime-notices'
New-Item -ItemType Directory -Force $destination | Out-Null
$manifest = @()
# Fetch license text, never executable code. Tags match the frameworks actually bundled.
foreach ($component in @('runtime', 'wpf', 'winforms')) {
    $version = if ($component -eq 'runtime') { $coreVersion } else { $desktopVersion }
    foreach ($file in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        $url = "https://raw.githubusercontent.com/dotnet/$component/v$version/$file"
        $output = Join-Path $destination "dotnet-$component-$version-$file"
        Invoke-WebRequest -Uri $url -OutFile $output -UseBasicParsing -TimeoutSec 30
        if ((Get-Item $output).Length -lt 200) { throw "Unexpectedly short license file: $url" }
        $manifest += [PSCustomObject]@{
            Component = $component
            Version = $version
            File = (Split-Path $output -Leaf)
            Source = $url
            SHA256 = (Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination 'sources.json') -Encoding utf8
if ($manifest.Count -ne 6) { throw 'Incomplete runtime license set.' }
Write-Host "Included six notice files: .NET $coreVersion / Windows Desktop $desktopVersion ($Runtime)."
