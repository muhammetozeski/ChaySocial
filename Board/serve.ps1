# Minimal review board host: serves board.html, streams board.json, appends replies to replies.json.
[CmdletBinding()]
param(
    [int]$Port = 5099
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$boardFile = Join-Path $root 'board.json'
$replyFile = Join-Path $root 'replies.json'
$pageFile = Join-Path $root 'board.html'

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Output "Review board listening on http://localhost:$Port/"

function Write-Body {
    param($Context, [string]$Text, [string]$ContentType = 'application/json; charset=utf-8', [int]$Status = 200)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $Context.Response.StatusCode = $Status
    $Context.Response.ContentType = $ContentType
    $Context.Response.Headers.Add('Cache-Control', 'no-store')
    $Context.Response.ContentLength64 = $bytes.Length
    $Context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $Context.Response.OutputStream.Close()
}

while ($listener.IsListening) {
    try {
        $context = $listener.GetContext()
        $path = $context.Request.Url.AbsolutePath

        switch -Regex ($path) {
            '^/data$' {
                $payload = if (Test-Path $boardFile) { Get-Content $boardFile -Raw -Encoding UTF8 } else { '{"cards":[]}' }
                Write-Body $context $payload
            }
            '^/replies$' {
                $payload = if (Test-Path $replyFile) { Get-Content $replyFile -Raw -Encoding UTF8 } else { '[]' }
                Write-Body $context $payload
            }
            '^/reply$' {
                $reader = [System.IO.StreamReader]::new($context.Request.InputStream, [System.Text.Encoding]::UTF8)
                $raw = $reader.ReadToEnd()
                $reader.Close()

                $incoming = $raw | ConvertFrom-Json
                $entry = [ordered]@{
                    cardId  = $incoming.cardId
                    verdict = $incoming.verdict
                    note    = $incoming.note
                    at      = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
                }

                $existing = @()
                if (Test-Path $replyFile) {
                    $parsed = Get-Content $replyFile -Raw -Encoding UTF8 | ConvertFrom-Json
                    if ($null -ne $parsed) { $existing = @($parsed) }
                }
                $existing += [pscustomobject]$entry
                ConvertTo-Json @($existing) -Depth 5 | Set-Content $replyFile -Encoding UTF8

                Write-Body $context '{"ok":true}'
            }
            default {
                $html = if (Test-Path $pageFile) { Get-Content $pageFile -Raw -Encoding UTF8 } else { '<h1>board.html not found</h1>' }
                Write-Body $context $html 'text/html; charset=utf-8'
            }
        }
    }
    catch {
        Write-Output "Board request failed: $($_.Exception.Message)"
    }
}
