# UserPromptSubmit hook for the ChaySocial autonomous loop.
#
# Clears the consecutive-block counter whenever the user types something. The cap in keep-loop-going.ps1 exists
# to free a loop that is spinning on its own; a person who is still talking to the session is not that, so their
# message resets the count and the loop gets its full allowance again.

$ErrorActionPreference = 'SilentlyContinue'

$counterPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Brainstorm\.loop-blocks'
Remove-Item -LiteralPath $counterPath -Force -ErrorAction SilentlyContinue

Write-Output '{}'
