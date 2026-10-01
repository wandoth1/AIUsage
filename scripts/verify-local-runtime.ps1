param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
$Executable = [IO.Path]::GetFullPath($Executable)
New-Item -ItemType Directory -Force $Destination | Out-Null
$fixture = Join-Path $Destination 'local-fixture'
$data = Join-Path $fixture 'app-data'
$home = Join-Path $fixture 'codex-synthetic'
$sessions = Join-Path $home 'sessions'
New-Item -ItemType Directory -Force $data, $sessions | Out-Null
$at = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O')
$rows = @(
    @{timestamp=$at; type='session_meta'; payload=@{id='synthetic-local-smoke'}},
    @{timestamp=$at; type='turn_context'; payload=@{model='gpt-5.6-sol'}},
    @{timestamp=$at; type='event_msg'; payload=@{type='token_count'; info=@{total_token_usage=@{input_tokens=1000; output_tokens=100; total_tokens=1100}}}}
)
$log = Join-Path $sessions 'rollout.jsonl'
($rows | ForEach-Object { $_ | ConvertTo-Json -Depth 8 -Compress }) | Set-Content $log -Encoding utf8NoBOM
@{CodexHome=$home; OnlineQuota=$true; Language='es'; RefreshSeconds=60; LightTheme=$false} | ConvertTo-Json | Set-Content (Join-Path $data 'settings.json') -Encoding utf8NoBOM
$auth = Join-Path $home 'auth.json'
$config = Join-Path $home 'config.toml'
[IO.File]::WriteAllText($auth, 'INACCESSIBLE-SYNTHETIC-CREDENTIAL-FIXTURE')
[IO.File]::WriteAllText($config, 'UNCHANGED-SYNTHETIC-CONFIGURATION')
$hashes = @{}
foreach ($file in @($auth, $config, $log)) { $hashes[$file] = (Get-FileHash $file -Algorithm SHA256).Hash }
$authLock = [IO.File]::Open($auth, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$configLock = [IO.File]::Open($config, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $p = Start-Process -FilePath $Executable -ArgumentList @('--smoke-test', ('"' + $Destination + '"'), '--local-fixture', ('"' + $data + '"')) -PassThru
    if (-not $p.WaitForExit(90000)) { $p.Kill(); throw 'Local and bilingual WPF smoke test timed out.' }
    if ($p.ExitCode -ne 0) { throw "WPF smoke test exit code $($p.ExitCode)" }
} finally { $configLock.Dispose(); $authLock.Dispose() }
foreach ($file in $hashes.Keys) { if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $hashes[$file]) { throw 'A synthetic source file was modified.' } }
if ((Get-Content (Join-Path $data 'settings.json') -Raw) -match 'OnlineQuota') { throw 'Legacy account toggle survived settings migration.' }
foreach ($marker in @('ui-smoke-ok.txt', 'local-smoke-ok.txt')) { if (-not (Test-Path (Join-Path $Destination $marker))) { throw "Missing marker: $marker" } }
'PASS Published x64 normal workflow with legacy online=true, locked synthetic credentials/config, unchanged source hashes, local-only Settings, and bilingual demo/rendering checks.' | Set-Content (Join-Path $Destination 'local-runtime-test.txt') -Encoding utf8NoBOM
# Do not upload the synthetic credential/settings directory with screenshots.
Remove-Item $fixture -Recurse -Force
