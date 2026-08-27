# Puts the running dev server on the internet, so the site can be opened from a phone.
#
#   pwsh tools/share.ps1
#
# Start the app first (dotnet run --launch-profile https), then this. It prints a
# https://<words>.trycloudflare.com address that works from any network — cellular, someone else's
# wifi, anywhere. No account and no signup: Cloudflare quick tunnels are anonymous.
#
# Two things here are not obvious, and both cost an afternoon to rediscover:
#
#   The HTTPS port, not the HTTP one. With both ports bound, ASP.NET can resolve an HTTPS port, so
#   UseHttpsRedirection() goes live and http://localhost:5235 answers 307 -> https://localhost:7224.
#   Tunnel the HTTP port and the phone is handed a redirect to localhost, which on a phone is the
#   phone.
#
#   127.0.0.1, not localhost. cloudflared resolves "localhost" to [::1] first and Kestrel may only
#   be listening on IPv4, which surfaces as a 502 Bad Gateway with "connection refused" buried in
#   the log rather than anything about IPv6.
#
# --no-tls-verify is required and harmless: the origin is this machine behind the ASP.NET
# development certificate, which is self-signed. The hop that matters — phone to Cloudflare — is
# real HTTPS with a real certificate.
#
# The URL is random and changes every run, and the tunnel dies with this process or when the
# machine sleeps. A stable address needs a Cloudflare account and a domain, or deploying to Azure.

[CmdletBinding()]
param(
    # Checked in order. The HTTPS port has to come first — see the note above.
    [int[]] $Ports = @(7224, 5235),

    [string] $Cloudflared = 'C:\Program Files (x86)\cloudflared\cloudflared.exe',

    # Keep running in this window. Without it the tunnel is detached and survives the shell.
    [switch] $Wait
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Cloudflared)) {
    if (Get-Command cloudflared -ErrorAction SilentlyContinue) {
        $Cloudflared = (Get-Command cloudflared).Source
    }
    else {
        Write-Error "cloudflared not found. Install it with: winget install --id Cloudflare.cloudflared"
    }
}

$port = $Ports | Where-Object {
    Get-NetTCPConnection -LocalPort $_ -State Listen -ErrorAction SilentlyContinue
} | Select-Object -First 1

if (-not $port) {
    Write-Error "Nothing is listening on $($Ports -join ' or '). Start the app first: dotnet run --project src/CharterTrip.Web --launch-profile https"
}

$scheme = if ($port -eq 7224) { 'https' } else { 'http' }
$origin = "${scheme}://127.0.0.1:$port"
Write-Host "Origin:  $origin"

$log = Join-Path $env:TEMP "chartertrip-tunnel.log"
Remove-Item $log, "$log.out" -Force -ErrorAction SilentlyContinue

$tunnelArgs = @('tunnel', '--url', $origin)
if ($scheme -eq 'https') { $tunnelArgs += '--no-tls-verify' }

$proc = Start-Process -FilePath $Cloudflared -ArgumentList $tunnelArgs `
    -RedirectStandardError $log -RedirectStandardOutput "$log.out" `
    -WindowStyle Hidden -PassThru

# cloudflared runs a connectivity precheck before it registers, so the URL takes a few seconds.
$url = $null
foreach ($attempt in 1..30) {
    Start-Sleep -Seconds 1
    if (Test-Path $log) {
        $match = Select-String -Path $log -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -ErrorAction SilentlyContinue |
                 Select-Object -First 1
        if ($match) { $url = $match.Matches.Value; break }
    }
    if ($proc.HasExited) { Write-Error "cloudflared exited. Log: $log" }
}

if (-not $url) { Write-Error "No tunnel URL after 30s. Log: $log" }

# Prove it reaches the app rather than just standing up: a 502 here means the origin is wrong.
try {
    $health = Invoke-RestMethod "$url/healthz" -TimeoutSec 20
    Write-Host "Health:  $($health.status), $($health.people) people, revision $($health.revision)"
}
catch {
    Write-Warning "Tunnel is up but /healthz did not answer. Log: $log"
}

Write-Host ""
Write-Host "  $url" -ForegroundColor Green
Write-Host ""
Write-Host "pid $($proc.Id) · log $log · stop with: Stop-Process -Id $($proc.Id)"

if ($Wait) {
    Write-Host "Ctrl+C to stop the tunnel."
    Wait-Process -Id $proc.Id
}
