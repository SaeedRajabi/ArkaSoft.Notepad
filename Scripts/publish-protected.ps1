# ==========================================================================
#  ArkaSoft Notepad - protected release publisher
#  Produces a single, compressed, self-contained executable with the
#  obfuscated assembly inside, then verifies the protection actually took.
#
#  Usage:  powershell -ExecutionPolicy Bypass -File Scripts\publish-protected.ps1
# ==========================================================================

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "Src\ArkaSoft.Notepad.UI\ArkaSoft.Notepad.UI.csproj"
$outDir = Join-Path $repo "publish"

Write-Host "==> Publishing (Release, win-x64, single-file, self-contained)..." -ForegroundColor Cyan
dotnet publish $project `
    -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $outDir
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# The obfuscation itself runs inside the build (ObfuscateRelease target).
# Verify on the pre-bundle assembly that the protection is really applied.
$dll = Get-ChildItem -Path (Join-Path $repo "Src\ArkaSoft.Notepad.UI\obj\Release") `
       -Recurse -Filter "ArkaSoft.Notepad.dll" |
       Where-Object { $_.FullName -match "win-x64" } |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($dll) {
    $bytes = [System.IO.File]::ReadAllBytes($dll.FullName)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)

    $mustBeGone = @("ApplyTypedTextDirection", "SaveSessionSnapshot", "WireFindPanel",
                    "PerformReplaceAll", "settings.json")
    $leaked = @()
    foreach ($name in $mustBeGone) {
        if ($ascii.Contains($name)) { $leaked += $name }
    }
    if ($leaked.Count -gt 0) {
        throw "PROTECTION FAILED - readable symbols found: $($leaked -join ', ')"
    }
    Write-Host "==> Protection verified: no private symbols / plaintext settings path in assembly" -ForegroundColor Green
}
else {
    Write-Host "!! pre-bundle dll not found - skipping deep verification" -ForegroundColor Yellow
}

$exe = Join-Path $outDir "ArkaSoft.Notepad.exe"
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "==> DONE: $exe ($size MB)" -ForegroundColor Green
    Write-Host "    This single exe is the complete app (runtime included, obfuscated, no PDBs)."
}
else {
    throw "expected exe not found in $outDir"
}
