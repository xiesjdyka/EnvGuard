param(
    [string]$AppPath = (Join-Path $PSScriptRoot '..\out\EnvGuard.exe'),
    [int]$ProxyPort = 10808,
    [string]$ExpectedIp = '',
    [int]$TimeoutMs = 2500,
    [ValidateSet('Setup','Monitor')][string]$Mode = 'Setup'
)
$ErrorActionPreference = 'Stop'
# Run against the same .NET Framework as the portable application, not PS7's .NET.
if ($PSVersionTable.PSVersion.Major -ge 6) {
    & (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoLogo -NoProfile -NonInteractive -File $PSCommandPath -AppPath $AppPath -ProxyPort $ProxyPort -ExpectedIp $ExpectedIp -TimeoutMs $TimeoutMs -Mode $Mode
    exit $LASTEXITCODE
}
Add-Type -AssemblyName System.Net.Http
[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AppPath).Path)
$guardPreviousTls = [Net.ServicePointManager]::SecurityProtocol
$guardReservedSocket = $null
try {
    # Regression: the host still has the old SSL3/TLS1.0 defaults that caused failure.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Ssl3 -bor [Net.SecurityProtocolType]::Tls
    $guardProfile = New-Object EnvGuard.Profile
    $guardProfile.ProxyPort = $ProxyPort
    $guardProfile.NetworkTimeoutMs = $TimeoutMs
    $guardProfile.ExitIp = $ExpectedIp
    for ($guardRound = 1; $guardRound -le 3; $guardRound++) {
        $guardChecker = New-Object EnvGuard.EnvironmentChecker($guardProfile)
        $guardWatch = [Diagnostics.Stopwatch]::StartNew()
        $guardTask = if ($Mode -eq 'Setup') { $guardChecker.NetworkForSetup([Threading.CancellationToken]::None) } else { $guardChecker.Network([Threading.CancellationToken]::None) }
        $guardResult = $guardTask.GetAwaiter().GetResult()
        if (-not $guardResult.Healthy -or $guardResult.Addresses[0] -ne $guardResult.Addresses[1]) {
            throw ('HTTPS probe failed: ' + (($guardResult.Confirmed + $guardResult.Unconfirmed) -join '; '))
        }
        [pscustomobject]@{Round=$guardRound; Exit1=$guardResult.Addresses[0]; Exit2=$guardResult.Addresses[1]; ElapsedMs=$guardWatch.ElapsedMilliseconds; Healthy=$true} | ConvertTo-Json -Compress
    }
    # Bind but do not listen: an unavailable proxy must never produce a direct IP.
    $guardReservedSocket = New-Object Net.Sockets.Socket([Net.Sockets.AddressFamily]::InterNetwork,[Net.Sockets.SocketType]::Stream,[Net.Sockets.ProtocolType]::Tcp)
    $guardReservedSocket.ExclusiveAddressUse = $true
    $guardReservedSocket.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback,0)))
    $guardProfile.ProxyPort = $guardReservedSocket.LocalEndPoint.Port
    $guardChecker = New-Object EnvGuard.EnvironmentChecker($guardProfile)
    $guardFailure = $guardChecker.Network([Threading.CancellationToken]::None).GetAwaiter().GetResult()
    if ($guardFailure.Healthy -or $guardFailure.Unconfirmed.Count -ne 2 -or @($guardFailure.Addresses | Where-Object { $null -ne $_ }).Count -ne 0) {
        throw 'Unavailable-proxy test failed: unexpected successful exit check.'
    }
    Write-Output 'PASS: TLS regression with 3 real dual-exit checks; unavailable proxy rejected with no direct fallback.'
} finally {
    if ($null -ne $guardReservedSocket) { $guardReservedSocket.Dispose() }
    [Net.ServicePointManager]::SecurityProtocol = $guardPreviousTls
}
