param([Parameter(Mandatory)][string]$PublishDirectory, [Parameter(Mandatory)][string]$Runtime)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dir = [IO.Path]::GetFullPath($PublishDirectory)
$config = Get-Content (Join-Path $dir 'AIUsage.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.configProperties.'System.StartupHookProvider.IsSupported' -ne $false) { throw 'Startup hooks must be disabled in the shipped runtimeconfig.' }
foreach ($file in @('AIUsage.exe', 'AIUsage.dll', 'AIUsage.Core.dll', 'coreclr.dll', 'wpfgfx_cor3.dll', 'es/AIUsage.Core.resources.dll')) {
    if (-not (Test-Path (Join-Path $dir $file))) { throw "Missing folder-deployment file: $file" }
}
$intermediate = Join-Path $root "src/AIUsage.Windows/bin/Release/net10.0-windows/$Runtime/AIUsage.dll"
if ((Get-FileHash $intermediate).Hash -ne (Get-FileHash (Join-Path $dir 'AIUsage.dll')).Hash) { throw 'Published application assembly differs from the built assembly.' }
$files = Get-ChildItem $dir -File -Recurse | Where-Object { $_.Name -ne 'PAYLOAD-SHA256.json' } | Sort-Object FullName
$manifest = @($files | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($dir, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $dir 'PAYLOAD-SHA256.json') -Encoding utf8NoBOM
Write-Output "PASS $Runtime inspectable folder payload, native WPF dependencies, disabled startup hooks, exact application-assembly hash; $($manifest.Count) file hashes recorded."
