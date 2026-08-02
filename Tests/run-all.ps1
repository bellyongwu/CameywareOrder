# Runs the whole assertion suite. Build first, then every harness, then the source sweeps.
#
#   pwsh Tests\run-all.ps1
#
# Exits non-zero if ANY of them fails, so it can gate a release without anybody reading the output.
# Run it before every release: the point of a suite is that it is run whole. Running harnesses
# selectively is how one of them stayed red for weeks here.

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$failed = @()

function Step($name, [scriptblock] $body) {
    # One string, built before the call. `Write-Host "a" + "b"` passes THREE arguments and prints
    # only the first, which is how a header can silently lose half of itself.
    $rule = '-' * [Math]::Max(3, 60 - $name.Length)
    Write-Host ""
    Write-Host "-- $name $rule" -ForegroundColor Cyan

    & $body
    if ($LASTEXITCODE -ne 0) { $script:failed += $name }
}

# The application first. Every harness ProjectReferences it, so a broken build is reported once here
# rather than three times as an unrelated-looking harness failure.
Step "build" {
    dotnet build CameywareOrder.csproj -v quiet --nologo -nodeReuse:false 2>&1 |
        Select-String -Pattern "error|warning|Build succeeded|Build FAILED"
}

if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "BUILD FAILED — the harnesses were not run." -ForegroundColor Red
    Write-Host "If the errors name missing obj\**\*.g.cs files, that is MSBuild node reuse holding" -ForegroundColor Yellow
    Write-Host "stale state, not a code fault:  dotnet build-server shutdown; rm -r obj" -ForegroundColor Yellow
    exit 1
}

Step "datacheck  (recycle bin, order query, CSV, backup schedule)" {
    dotnet run --project Tests\DataCheck\DataCheck.csproj -v quiet --nologo -nodeReuse:false
}

Step "democheck  (demo store, seeded history, copy shop)" {
    dotnet run --project Tests\DemoCheck\DemoCheck.csproj -v quiet --nologo -nodeReuse:false
}

# Constructs real windows, so it needs a desktop session. It asserts it does not touch the user's
# own credentials.json or roles.json, and writes its screenshots to Tests\.artifacts\renders.
Step "uicheck    (renders + copy/paste commands + menu structure)" {
    dotnet run --project Tests\UiCheck\UiCheck.csproj -v quiet --nologo -nodeReuse:false
}

Step "keycheck   (string-table parity across every language)" {
    node Tests\Scripts\keycheck.js "$repo\Settings\System\Languages"
}

Step "surfacecheck (copy/paste surfaces, no stray shortcuts, no CJK outside the language files)" {
    node Tests\Scripts\surfacecheck.js "$repo"
}

Write-Host ""
if ($failed.Count -eq 0) {
    Write-Host "SUITE GREEN" -ForegroundColor Green
    exit 0
}

Write-Host ("SUITE RED — failed: " + ($failed -join ", ")) -ForegroundColor Red
exit 1
