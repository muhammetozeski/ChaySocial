# Stop hook for the ChaySocial autonomous loop.
#
# Runs at the one moment that matters: when the turn is about to end. Unlike a cron job -- which the harness
# only fires while the session is idle, and which measurably did not fire at all across a three hour idle
# window -- this is invoked by the stop itself, so it cannot be missed.
#
# It has three independent brakes, and any one of them lets the session stop normally:
#   1. the sentinel file must exist        -- delete it and the loop is over
#   2. the deadline inside it must be ahead -- it expires on its own
#   3. the consecutive-block count must be under the cap -- a stuck loop frees itself
# Anything unexpected also lets the session stop: a broken guard must never trap somebody in their own terminal.

$ErrorActionPreference = 'Stop'

# Most consecutive turns this may force before it lets go. Reset every time the user types something, so this
# only counts turns nobody asked for.
$MostConsecutiveBlocks = 40

$projectRoot = Split-Path -Parent $PSScriptRoot
$sentinelPath = Join-Path $projectRoot 'Brainstorm\.loop-active'
$counterPath = Join-Path $projectRoot 'Brainstorm\.loop-blocks'
$inboxPath = Join-Path $projectRoot 'Brainstorm\Inbox'

function Grant-Stop {
    Write-Output '{}'
    exit 0
}

try {
    if (-not (Test-Path -LiteralPath $sentinelPath)) { Grant-Stop }

    $deadlineUnix = 0
    $sentinelText = (Get-Content -LiteralPath $sentinelPath -Raw).Trim()
    if (-not [long]::TryParse($sentinelText, [ref]$deadlineUnix)) { Grant-Stop }

    if ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() -gt $deadlineUnix) {
        Remove-Item -LiteralPath $sentinelPath -Force -ErrorAction SilentlyContinue
        Grant-Stop
    }

    $waiting = @(Get-ChildItem -LiteralPath $inboxPath -Filter *.md -ErrorAction SilentlyContinue)
    if ($waiting.Count -eq 0) { Grant-Stop }

    $blocksSoFar = 0
    if (Test-Path -LiteralPath $counterPath) {
        $counterText = (Get-Content -LiteralPath $counterPath -Raw).Trim()
        [void][int]::TryParse($counterText, [ref]$blocksSoFar)
    }

    $blocksSoFar++
    if ($blocksSoFar -gt $MostConsecutiveBlocks) {
        Remove-Item -LiteralPath $counterPath -Force -ErrorAction SilentlyContinue
        Grant-Stop
    }

    Set-Content -LiteralPath $counterPath -Value $blocksSoFar -NoNewline

    $oldest = ($waiting | Sort-Object Name | Select-Object -First 1).BaseName
    $reason = "The autonomous loop is still running and $($waiting.Count) ideas are waiting in Brainstorm/Inbox. " +
              "Do not stop. Read the disk first (Get-Date, git log --oneline -6, git status --short, the Inbox " +
              "listing), finish any half-done work, then take the oldest idea -- '$oldest' -- and build it: " +
              "apply, build with dotnet build ChaySocial.Web\ChaySocial.Web.csproj -t:Compile, verify it really " +
              "works in a browser against the running server, commit one concern, push, and move the idea to " +
              "Brainstorm/Applied. Ask the user nothing. This is forced continuation $blocksSoFar of " +
              "$MostConsecutiveBlocks; delete Brainstorm/.loop-active to end the loop."

    Write-Output (@{ decision = 'block'; reason = $reason } | ConvertTo-Json -Compress)
}
catch {
    Write-Output '{}'
}
