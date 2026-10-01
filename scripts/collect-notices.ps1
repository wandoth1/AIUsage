param(
    [Parameter(Mandatory = $true)][ValidateSet('win-x64', 'win-arm64')][string]$Runtime,
    [Parameter(Mandatory = $true)][string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assetsPath = Join-Path $root 'src/AIUsage.Windows/obj/project.assets.json'
$assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
$version = $null
$pattern = '^(?:runtimepack\.)?Microsoft\.NETCore\.App\.Runtime\.' + [regex]::Escape($Runtime) + '/(\d+\.\d+\.\d+)$'
foreach ($name in $assets.libraries.PSObject.Properties.Name) {
    if ($name -match $pattern) { $version = $Matches[1]; break }
}
if (-not $version) { throw "Cannot determine the resolved .NET runtime version for $Runtime from project.assets.json." }
$destination = Join-Path $PublishDirectory 'runtime-notices'
New-Item -ItemType Directory -Force $destination | Out-Null
$manifest = @()
# Fetch license text, never executable code. Tags match the runtime actually selected by restore.
foreach ($component in @('runtime', 'wpf', 'winforms')) {
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
Write-Host "Included all six runtime/WPF/WinForms license and notice files for .NET $version ($Runtime)."
