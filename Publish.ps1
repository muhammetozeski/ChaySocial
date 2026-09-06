# Builds every shipping artifact: the server, and a client for each platform and architecture.
#
# Two things here are not obvious and both were learned the hard way.
#
# Android never takes -r or -p:RuntimeIdentifiers. Those are global MSBuild properties, so they reach
# MainProject, mark the library RID-specific, and the Android SDK then runs its per-RID inner build with
# SkipCompilerExecution=true. The source generator never runs and the build fails with "NavigationConstants
# does not exist" — an error that looks nothing like its cause. The project exposes AndroidRuntimeIdentifiers
# instead, which is its own property and does not travel.
#
# Android's 32-bit ABIs exist only as Mono runtime packs, so they need -p:UseMonoRuntime=true. That IS a
# global property, and restore covers every target framework a project declares even when -f names one of
# them, so it sends the Windows head looking for a Microsoft.NETCore.App.Runtime.Mono.win-x64 that .NET 10
# does not publish. -p:SkipWindowsHead=true drops that head for those two builds.

$ErrorActionPreference = "Stop"

$SlnDir = $PSScriptRoot
$PublishDir = Join-Path $SlnDir "Publish"
$MauiCsproj = Join-Path $SlnDir "ChaySocial\ChaySocial.csproj"
$WebCsproj = Join-Path $SlnDir "ChaySocial.Web\ChaySocial.Web.csproj"
$WebClientCsproj = Join-Path $SlnDir "ChaySocial.Web.Client\ChaySocial.Web.Client.csproj"
$WindowsFramework = "net10.0-windows10.0.19041.0"
$AndroidFramework = "net10.0-android"

function Invoke-Publish {
    param([string]$Label, [string[]]$Arguments, [string]$Output)

    Write-Host "  $Label" -ForegroundColor Cyan
    & dotnet publish @Arguments -o $Output --nologo -v q

    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
}

# ── A clean start ──
# Stale bin and obj directories are what let a build succeed on one machine and fail on the next.
Write-Host "Clearing previous output..." -ForegroundColor Yellow
foreach ($stale in @("bin", "obj")) {
    Get-ChildItem $SlnDir -Recurse -Directory -Filter $stale -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\\.git\\' } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
}
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir | Out-Null

# ── Server ──
Write-Host "Server" -ForegroundColor Green
foreach ($mode in @(@{ Name = "SelfContained"; Value = "true" }, @{ Name = "FrameworkDependent"; Value = "false" })) {
    Invoke-Publish `
        -Label "win-x64 $($mode.Name)" `
        -Arguments @($WebCsproj, "-c", "Release", "-r", "win-x64", "--self-contained", $mode.Value) `
        -Output "$PublishDir\Server\win-x64\$($mode.Name)"
}

# ── Web client ──
# One output, not two: Blazor WebAssembly refuses --self-contained false with NETSDK1102, because a browser has
# no shared runtime to depend on. A "SelfContained" folder next to nothing would be a distinction that is not
# there.
Write-Host "Web client" -ForegroundColor Green
Invoke-Publish `
    -Label "browser" `
    -Arguments @($WebClientCsproj, "-c", "Release") `
    -Output "$PublishDir\Clients\Web"

# ── Windows clients ──
# win-arm32 is absent on purpose: .NET 10's portable RID graph carries win-x64, win-x86 and win-arm64 only,
# and asking for win-arm fails with NETSDK1083.
Write-Host "Windows clients" -ForegroundColor Green
foreach ($rid in @("win-x64", "win-x86", "win-arm64")) {
    foreach ($mode in @(@{ Name = "SelfContained"; Value = "true" }, @{ Name = "FrameworkDependent"; Value = "false" })) {
        Invoke-Publish `
            -Label "$rid $($mode.Name)" `
            -Arguments @($MauiCsproj, "-f", $WindowsFramework, "-c", "Release", "-r", $rid, "--self-contained", $mode.Value) `
            -Output "$PublishDir\Clients\Windows\$rid\$($mode.Name)"
    }
}

# ── Android clients ──
# One output per architecture. An APK always carries its own runtime, so --self-contained changes nothing: the
# two forms were built once and came out identical to the byte, same SHA-256, same AOT libraries inside. Two
# folders holding the same file is not a choice anybody can make.
Write-Host "Android clients" -ForegroundColor Green
$AndroidAbis = @(
    @{ Rid = "android-arm64"; NeedsMono = $false },
    @{ Rid = "android-x64";   NeedsMono = $false },
    @{ Rid = "android-arm";   NeedsMono = $true },
    @{ Rid = "android-x86";   NeedsMono = $true }
)
foreach ($abi in $AndroidAbis) {
    $arguments = @(
        $MauiCsproj, "-f", $AndroidFramework, "-c", "Release",
        "-p:AndroidRuntimeIdentifiers=$($abi.Rid)",
        "-p:AndroidKeyStore=false"
    )
    if ($abi.NeedsMono) { $arguments += @("-p:SkipWindowsHead=true", "-p:UseMonoRuntime=true") }

    Invoke-Publish `
        -Label $abi.Rid `
        -Arguments $arguments `
        -Output "$PublishDir\Clients\Android\$($abi.Rid)"
}

# ── What came out ──
Write-Host ""
Write-Host "Published:" -ForegroundColor Green
$Artifacts = @("$PublishDir\Server\win-x64\SelfContained", "$PublishDir\Server\win-x64\FrameworkDependent", "$PublishDir\Clients\Web")
foreach ($rid in @("win-x64", "win-x86", "win-arm64")) {
    $Artifacts += @("$PublishDir\Clients\Windows\$rid\SelfContained", "$PublishDir\Clients\Windows\$rid\FrameworkDependent")
}
foreach ($abi in $AndroidAbis) { $Artifacts += "$PublishDir\Clients\Android\$($abi.Rid)" }

foreach ($artifact in $Artifacts) {
    $files = Get-ChildItem $artifact -Recurse -File
    $megabytes = ($files | Measure-Object Length -Sum).Sum / 1MB
    "  {0,-46} {1,5} files {2,8:N1} MB" -f $artifact.Replace("$PublishDir\", ""), $files.Count, $megabytes
}
