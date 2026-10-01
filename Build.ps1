param([switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.8 的编译器不可用；需要 Windows x64 + .NET Framework 4.8。' }
$outputDir = Join-Path $projectDir 'out'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | ForEach-Object FullName)
$programPath = Join-Path $outputDir 'EnvGuard.exe'
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/main:EnvGuard.Program',"/out:$programPath",('/win32manifest:' + (Join-Path $projectDir 'app.manifest')), '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Net.Http.dll','/r:System.Management.dll','/r:System.ServiceProcess.dll') + $sourceFiles
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '程序编译失败。' }
if ($Test) {
    & $compilerPath /nologo /target:winexe /platform:x64 /optimize+ ("/out:" + (Join-Path $outputDir 'Fixture.exe')) (Join-Path $projectDir 'tests\Fixture.cs')
    if ($LASTEXITCODE -ne 0) { throw '测试夹具编译失败。' }
    $testDir = Join-Path $projectDir ('test-output\' + [guid]::NewGuid().ToString('N'))
    $testProcess = Start-Process -FilePath $programPath -ArgumentList @('--self-test', ('"' + $testDir + '"')) -PassThru -Wait -WindowStyle Hidden
    Get-Content -LiteralPath (Join-Path $testDir 'results.txt')
    if ($testProcess.ExitCode -ne 0) { throw '自测失败。' }
    $uiDir = Join-Path $testDir 'ui'
    $uiProcess = Start-Process -FilePath $programPath -ArgumentList @('--ui-smoke-test', ('"' + $uiDir + '"')) -PassThru -Wait -WindowStyle Hidden
    if ($uiProcess.ExitCode -ne 0) { throw '界面渲染测试失败。' }
    Get-Content -LiteralPath (Join-Path $uiDir 'ui-result.txt')
    & (Join-Path $projectDir 'ChromePrivacy.ps1') -Mode SelfTest
    & (Join-Path $projectDir 'ChromePrivacy.ps1') -Mode SelfTest -Preset Basic
    & (Join-Path $projectDir 'tests\ChromePresets.Tests.ps1')
    Write-Output "界面检查图片：$uiDir"
}
if ($Package) {
    $releaseDir = Join-Path $outputDir 'EnvGuard-portable'
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
    # Explicit allow-list: never package machine profiles, test data, logs or credentials.
    Copy-Item -LiteralPath $programPath -Destination $releaseDir
    Copy-Item -LiteralPath (Join-Path $projectDir 'README.md'),(Join-Path $projectDir 'LICENSE'),(Join-Path $projectDir 'CHROME-PRIVACY.md'),(Join-Path $projectDir 'ChromePrivacy.ps1'),(Join-Path $projectDir 'TECHNICAL.md'),(Join-Path $projectDir 'CHROME-DETAILS.md') -Destination $releaseDir
    & (Join-Path $projectDir 'Build-Guides.ps1') -OutputDirectory $releaseDir
    $archive = Join-Path $outputDir 'EnvGuard-windows-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $releaseDir 'EnvGuard.exe'),(Join-Path $releaseDir '先看这里.html'),(Join-Path $releaseDir 'Chrome浏览器怎么准备.html'),(Join-Path $releaseDir 'README.md'),(Join-Path $releaseDir 'LICENSE'),(Join-Path $releaseDir 'CHROME-PRIVACY.md'),(Join-Path $releaseDir 'ChromePrivacy.ps1'),(Join-Path $releaseDir 'TECHNICAL.md'),(Join-Path $releaseDir 'CHROME-DETAILS.md') -DestinationPath $archive -Force
    $digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $outputDir 'SHA256SUMS.txt'), "$digest  EnvGuard-windows-x64.zip`n", [Text.UTF8Encoding]::new($false))
    Write-Output "便携包：$archive"
}
