param(
    [string]$GameDir = "",
    [string]$LunarisLibDir = "",
    [switch]$BuildCompanionMods,
    # Compile against the real installed Erenshor/Lunaris assemblies and report the result (path,
    # SHA-256) WITHOUT copying the DLL into the live plugins folder and WITHOUT touching an already-
    # running game's loaded plugin. Use this to verify a candidate build before deciding to install it.
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Erenshor {
    param([string]$Explicit)
    if ($Explicit -and (Test-Path (Join-Path $Explicit "Erenshor.exe"))) { return (Resolve-Path $Explicit).Path }

    $candidates = New-Object System.Collections.Generic.List[string]
    if (${env:ProgramFiles(x86)}) { $candidates.Add((Join-Path ${env:ProgramFiles(x86)} "Steam\steamapps\common\Erenshor")) }
    if ($env:ProgramFiles) { $candidates.Add((Join-Path $env:ProgramFiles "Steam\steamapps\common\Erenshor")) }

    $steamRoots = @()
    if (${env:ProgramFiles(x86)}) { $steamRoots += (Join-Path ${env:ProgramFiles(x86)} "Steam") }
    if ($env:ProgramFiles) { $steamRoots += (Join-Path $env:ProgramFiles "Steam") }
    foreach ($steamRoot in $steamRoots) {
        $vdf = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $content = Get-Content $vdf -Raw
            [regex]::Matches($content, '"path"\s+"([^"]+)"') | ForEach-Object {
                $library = $_.Groups[1].Value -replace '\\\\','\'
                if ([System.IO.Directory]::Exists($library)) {
                    $candidates.Add([System.IO.Path]::Combine($library, "steamapps", "common", "Erenshor"))
                }
            }
        }
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path (Join-Path $candidate "Erenshor.exe")) { return (Resolve-Path $candidate).Path }
    }

    $manual = Read-Host "Could not auto-find Erenshor. Paste the folder containing Erenshor.exe"
    if ($manual -and (Test-Path (Join-Path $manual "Erenshor.exe"))) { return (Resolve-Path $manual).Path }
    throw "Erenshor installation not found."
}

function Find-Csc {
    $paths = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    foreach ($path in $paths) { if ($path -and (Test-Path $path)) { return $path } }
    throw "Could not find csc.exe. Install .NET Framework 4.8 Developer Pack or Visual Studio Build Tools, then rerun."
}

function Find-LunarisLibDir {
    param([string]$Explicit, [string]$DetectedGameDir)

    $candidates = New-Object System.Collections.Generic.List[string]
    if ($Explicit) { $candidates.Add($Explicit) }
    $candidates.Add((Join-Path $ScriptRoot "LunarisLibs"))
    $candidates.Add($DetectedGameDir)

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not $candidate) { continue }
        $lunaris = Join-Path $candidate "Lunaris.dll"
        $harmony = Join-Path $candidate "0Harmony.dll"
        if ((Test-Path $lunaris) -and (Test-Path $harmony)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Could not find Lunaris developer references. Put Lunaris.dll and 0Harmony.dll in '$ScriptRoot\LunarisLibs' or pass -LunarisLibDir."
}

function Add-ReferenceIfPresent {
    param([System.Collections.Generic.List[string]]$List, [string]$Path)
    if ($Path -and (Test-Path $Path)) { $List.Add((Resolve-Path $Path).Path) }
}

function Invoke-OptionalModBuild {
    param([string]$Name, [string]$Directory, [string]$DetectedGameDir)

    if (-not $BuildCompanionMods) { return }
    $build = Join-Path $Directory "BUILD_AND_INSTALL.ps1"
    if (-not (Test-Path -LiteralPath $Directory)) {
        Write-Host "Skipping $Name (directory not present)." -ForegroundColor DarkGray
        return
    }
    if (-not (Test-Path -LiteralPath $build)) {
        Write-Host "Skipping $Name (BUILD_AND_INSTALL.ps1 not present)." -ForegroundColor Yellow
        return
    }

    Write-Host ""
    Write-Host "Building optional companion mod: $Name" -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $build -GameDir $DetectedGameDir
    if ($LASTEXITCODE -ne 0) { throw "$Name build failed." }
}

$GameDir = Find-Erenshor $GameDir
$LunarisLibDir = Find-LunarisLibDir $LunarisLibDir $GameDir
$Csc = Find-Csc
$Managed = Join-Path $GameDir "Erenshor_Data\Managed"
$PluginRoot = Join-Path $GameDir "plugins"
$ConfigRoot = Join-Path $PluginRoot "config"
$MemoryDir = Join-Path $ConfigRoot "DeepSims\Memory"

$AssemblyCSharp = Join-Path $Managed "Assembly-CSharp.dll"
$JsonModule = Join-Path $Managed "UnityEngine.JSONSerializeModule.dll"
$UiModule = Join-Path $Managed "UnityEngine.UIModule.dll"
$TextRenderingModule = Join-Path $Managed "UnityEngine.TextRenderingModule.dll"
$UnityUi = Join-Path $Managed "UnityEngine.UI.dll"
$UnityEngineFacade = Join-Path $Managed "UnityEngine.dll"
$UnityEngineCore = Join-Path $Managed "UnityEngine.CoreModule.dll"
$Netstandard = Join-Path $Managed "netstandard.dll"
$LunarisDll = Join-Path $LunarisLibDir "Lunaris.dll"
$HarmonyDll = Join-Path $LunarisLibDir "0Harmony.dll"

foreach ($required in @($AssemblyCSharp, $JsonModule, $UnityEngineFacade, $UnityEngineCore, $UiModule, $TextRenderingModule, $UnityUi, $Netstandard, $LunarisDll, $HarmonyDll)) {
    if (-not (Test-Path $required)) { throw "Required reference not found: $required" }
}

New-Item -ItemType Directory -Force -Path $PluginRoot, $ConfigRoot, $MemoryDir | Out-Null

$Refs = New-Object System.Collections.Generic.List[string]
foreach ($required in @($LunarisDll, $HarmonyDll, $AssemblyCSharp, $JsonModule, $UnityEngineFacade, $UnityEngineCore, $UiModule, $TextRenderingModule, $UnityUi, $Netstandard)) { Add-ReferenceIfPresent $Refs $required }

$optionalNames = @(
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.AIModule.dll",
    "UnityEngine.InputLegacyModule.dll",
    "UnityEngine.PhysicsModule.dll",
    "UnityEngine.AnimationModule.dll",
    "UnityEngine.UIModule.dll",
    "UnityEngine.UI.dll",
    "UnityEngine.UnityWebRequestModule.dll",
    "UnityEngine.AudioModule.dll",
    "UnityEngine.ParticleSystemModule.dll",
    "Unity.TextMeshPro.dll",
    "netstandard.dll"
)
foreach ($name in $optionalNames) { Add-ReferenceIfPresent $Refs (Join-Path $Managed $name) }
foreach ($uiReference in @("UnityEngine.UIModule.dll", "UnityEngine.TextRenderingModule.dll", "UnityEngine.UI.dll")) {
    if (-not (Test-Path (Join-Path $Managed $uiReference))) { throw "Required fallback UI reference not found: $uiReference" }
}

$TempDir = Join-Path $env:TEMP ("ErenshorDeepSims-build-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
$TempDll = Join-Path $TempDir "ErenshorDeepSims.dll"
$Rsp = Join-Path $TempDir "ErenshorDeepSims.rsp"
$OutDll = Join-Path $PluginRoot "ErenshorDeepSims.dll"

try {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("/nologo")
    $lines.Add("/target:library")
    $lines.Add("/optimize+")
    $lines.Add('/out:"' + $TempDll + '"')
    foreach ($r in ($Refs | Select-Object -Unique)) { $lines.Add('/reference:"' + $r + '"') }
    Get-ChildItem (Join-Path $ScriptRoot "src") -Filter "*.cs" | Sort-Object Name | ForEach-Object { $lines.Add('"' + $_.FullName + '"') }
    $fallbackUi = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptRoot)) "Erenshor-Mod-Suite\shared\ErenshorSuite.UI\StandaloneFallbackUi.cs"
    if (-not (Test-Path -LiteralPath $fallbackUi)) { throw "Missing shared standalone UI source: $fallbackUi" }
    $lines.Add('"' + $fallbackUi + '"')

    # Shared contract conformance tests are source-only and optional. They remain part of a normal
    # Deep Sims build when the directory is present, exactly as before the loader migration.
    $SharedDir = Join-Path $ScriptRoot "shared"
    if (Test-Path $SharedDir) {
        $lines.Add("/define:SHARED_CONTRACTS")
        Get-ChildItem $SharedDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $lines.Add('"' + $_.FullName + '"') }
    }
    $lines | Set-Content -Path $Rsp -Encoding ASCII

    $lunarisInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($LunarisDll)
    $lunarisHash = (Get-FileHash -Algorithm SHA256 -Path $LunarisDll).Hash.ToLowerInvariant()

    Write-Host "Building Deep Sims 0.8.2 Beta as a native Lunaris plugin..." -ForegroundColor Cyan
    Write-Host "  Game:    $GameDir"
    Write-Host "  Lunaris: $LunarisDll"
    Write-Host "  Version: $($lunarisInfo.FileVersion)"
    Write-Host "  SHA256:  $lunarisHash"
    Write-Host "  Compile reference SHA-256:" -ForegroundColor DarkCyan
    foreach ($referencePath in ($Refs | Select-Object -Unique)) {
        $referenceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $referencePath).Hash.ToLowerInvariant()
        Write-Host "    $([System.IO.Path]::GetFileName($referencePath))  $referenceHash"
    }

    & $Csc "@$Rsp"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed. Copy the compiler errors and send them to me." }
    if (-not (Test-Path $TempDll)) { throw "Compiler reported success but did not produce $TempDll" }

    if ($BuildOnly) {
        $BuildOutputDir = Join-Path $ScriptRoot "build-output"
        New-Item -ItemType Directory -Force -Path $BuildOutputDir | Out-Null
        $CandidateDll = Join-Path $BuildOutputDir "ErenshorDeepSims.dll"
        Copy-Item -LiteralPath $TempDll -Destination $CandidateDll -Force
        $candidateHash = (Get-FileHash -Algorithm SHA256 -Path $CandidateDll).Hash.ToLowerInvariant()
        Write-Host ""
        Write-Host "Deep Sims 0.8.2 Beta compiled successfully (BuildOnly - nothing installed)." -ForegroundColor Green
        Write-Host "  Candidate DLL: $CandidateDll"
        Write-Host "  SHA256:        $candidateHash"
        if (Test-Path $OutDll) {
            $installedHash = (Get-FileHash -Algorithm SHA256 -Path $OutDll).Hash.ToLowerInvariant()
            Write-Host "  Installed DLL: $OutDll"
            Write-Host "  Installed SHA256: $installedHash"
            Write-Host "  Match installed: $($candidateHash -eq $installedHash)"
        }
        else {
            Write-Host "  No DLL currently installed at $OutDll."
        }
    }
    else {
        $erenshorRunning = @(Get-Process -Name "Erenshor" -ErrorAction SilentlyContinue).Count -gt 0
        if ($erenshorRunning) {
            throw "Erenshor is currently running. Refusing to replace the installed plugin DLL while the game is running - close the game first, or rerun with -BuildOnly to just compile."
        }
        # Retain the exact candidate bytes, then install that same file so post-install SHA
        # verification compares one artifact rather than two separate compiler emissions.
        $BuildOutputDir = Join-Path $ScriptRoot "build-output"
        New-Item -ItemType Directory -Force -Path $BuildOutputDir | Out-Null
        $CandidateDll = Join-Path $BuildOutputDir "ErenshorDeepSims.dll"
        Copy-Item -LiteralPath $TempDll -Destination $CandidateDll -Force
        Copy-Item -LiteralPath $CandidateDll -Destination $OutDll -Force
        $candidateHash = (Get-FileHash -Algorithm SHA256 -Path $CandidateDll).Hash.ToLowerInvariant()
        $installedHash = (Get-FileHash -Algorithm SHA256 -Path $OutDll).Hash.ToLowerInvariant()
        if ($candidateHash -ne $installedHash) { throw "Installed DLL hash does not match the retained candidate." }

        Write-Host ""
        Write-Host "Deep Sims 0.8.2 Beta installed as a native Lunaris plugin." -ForegroundColor Green
        Write-Host "  Plugin: $OutDll"
        Write-Host "  Candidate SHA256: $candidateHash"
        Write-Host "  Installed SHA256: $installedHash"
        Write-Host "  Match installed: $($candidateHash -eq $installedHash)"
        Write-Host "  Config: $ConfigRoot\erenshordeepsims.lpcfg"
        Write-Host "  Memory: $MemoryDir"
        Write-Host ""
        Write-Host "Lunaris runtime libraries are NOT copied into the plugin folder by this script."
        Write-Host "Use /aistatus, /dsims, /dssession, /dsperf, and the normal Deep Sims chat commands after launch."
    }
}
finally {
    if (Test-Path $TempDir) { Remove-Item -LiteralPath $TempDir -Recurse -Force -ErrorAction SilentlyContinue }
}

$WorkspaceModsDir = Split-Path -Parent $ScriptRoot
Invoke-OptionalModBuild "Erenshor Follow" (Join-Path $WorkspaceModsDir "ErenshorFollow") $GameDir
Invoke-OptionalModBuild "Practice Duels" (Join-Path $WorkspaceModsDir "Erenshor-Duel") $GameDir
Invoke-OptionalModBuild "Erenshor PvP" (Join-Path $WorkspaceModsDir "Erenshor-PvP") $GameDir
Invoke-OptionalModBuild "Erenshor Nemesis" (Join-Path $WorkspaceModsDir "Erenshor-Nemesis") $GameDir
Invoke-OptionalModBuild "Erenshor Party Tools" (Join-Path $WorkspaceModsDir "Erenshor-PartyTools") $GameDir
Invoke-OptionalModBuild "Erenshor Campmaster" (Join-Path $WorkspaceModsDir "Erenshor-Campmaster") $GameDir
Invoke-OptionalModBuild "Erenshor Crafting Expanded" (Join-Path $WorkspaceModsDir "Erenshor-Crafting-Expanded") $GameDir
