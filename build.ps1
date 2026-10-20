<#
.SYNOPSIS
    Build the NatoERA mod (NatoERA.dll).

.DESCRIPTION
    Compiles every .cs source file under src\ and writes the result to
    build\out\NatoERA.dll. The game path is guessed automatically; override it
    with -GhpcBin.

.PARAMETER GhpcBin
    The game's Bin directory. Defaults to E:\SteamLibrary\steamapps\common\Gunner HEAT PC\Bin

.PARAMETER Install
    After a successful build, copy the DLL to <GhpcBin>\Mods\ and the asset
    bundles to <GhpcBin>\Mods\NatoEraAssets\

.EXAMPLE
    .\build.ps1
    Build only.

.EXAMPLE
    .\build.ps1 -Install
    Build and install into the game directory.

.EXAMPLE
    .\build.ps1 -GhpcBin "D:\Steam\steamapps\common\Gunner HEAT PC\Bin" -Install
    Use a different game path and install.
#>
[CmdletBinding()]
param(
    [string]$GhpcBin = $null,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not $GhpcBin) {
    # environment variable first, then the default path, then scan the Steam library folders
    if ($env:GHPC_BIN) {
        $GhpcBin = $env:GHPC_BIN
    }
    else {
        $default = 'E:\SteamLibrary\steamapps\common\Gunner HEAT PC\Bin'
        if (Test-Path (Join-Path $default 'GHPC_Data\Managed\Assembly-CSharp.dll')) {
            $GhpcBin = $default
        }
        else {
            $candidates = @()
            $vdf = 'C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf'
            if (Test-Path $vdf) {
                $candidates = Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches |
                    ForEach-Object { $_.Matches } |
                    ForEach-Object { Join-Path ($_.Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Gunner HEAT PC\Bin' }
            }
            $hit = $candidates | Where-Object { Test-Path (Join-Path $_ 'GHPC_Data\Managed\Assembly-CSharp.dll') } | Select-Object -First 1
            if ($hit) { $GhpcBin = $hit }
        }
    }
}

if (-not $GhpcBin -or -not (Test-Path (Join-Path $GhpcBin 'GHPC_Data\Managed\Assembly-CSharp.dll'))) {
    throw "Game directory not found. Pass -GhpcBin `"<your Bin folder>`" or set the GHPC_BIN environment variable."
}

Write-Host "== NatoERA build ==" -ForegroundColor Cyan
Write-Host "  source dir : $root"
Write-Host "  game dir   : $GhpcBin"

$proj = Join-Path $root 'NatoERA.csproj'
$out  = Join-Path $root 'build\out'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

& dotnet build $proj -c Release -o $out --nologo /p:GhpcBin=$GhpcBin
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dll = Join-Path $out 'NatoERA.dll'
if (-not (Test-Path $dll)) { throw "The build reported success but $dll is missing." }

$size = (Get-Item $dll).Length
Write-Host ""
Write-Host "== Build succeeded ==" -ForegroundColor Green
Write-Host "  $dll ($size bytes)"

if ($Install) {
    Write-Host ""
    Write-Host "== Installing ==" -ForegroundColor Cyan

    $modsDir = Join-Path $GhpcBin 'Mods'
    New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
    Copy-Item $dll (Join-Path $modsDir 'NatoERA.dll') -Force
    Write-Host "  -> $modsDir\NatoERA.dll"

    $assetsDst = Join-Path $modsDir 'NatoEraAssets'
    New-Item -ItemType Directory -Path $assetsDst -Force | Out-Null
    $srcAssets = Join-Path $root 'assets\NatoEraAssets'
    foreach ($f in Get-ChildItem $srcAssets -File) {
        Copy-Item $f.FullName (Join-Path $assetsDst $f.Name) -Force
        Write-Host "  -> $assetsDst\$($f.Name)"
    }

    Write-Host ""
    Write-Host "== Install complete ==" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Tip: add -Install to copy straight into the game directory." -ForegroundColor DarkGray
}
