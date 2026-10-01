param(
    [string]$SourceFile=(Join-Path $PSScriptRoot '..\ChromePrivacy.ps1')
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

# Parse the source rather than executing its entry point. These tests never load
# Read-Policies / Write-Policies and never touch Chrome or the user's profile.
$taskTokens=$null
$taskParseErrors=$null
$taskAst=[System.Management.Automation.Language.Parser]::ParseFile(
    [IO.Path]::GetFullPath($SourceFile),[ref]$taskTokens,[ref]$taskParseErrors)
if($taskParseErrors.Count -ne 0){throw 'ChromePrivacy.ps1 contains parsing errors.'}
$taskFunctionNames=@('Validate-State','Assert-NoOtherPreset')
$taskFunctions=@($taskAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -in $taskFunctionNames
},$false))
if($taskFunctions.Count -ne 2 -or @($taskFunctions.Name | Sort-Object -Unique).Count -ne 2){
    throw 'Expected exactly Validate-State and Assert-NoOtherPreset in the source.'
}
foreach($taskFunction in $taskFunctions){. ([scriptblock]::Create($taskFunction.Extent.Text))}

$script:taskChecks=0
function Expect-Accept([string]$Name,[scriptblock]$Action){
    try{& $Action}catch{throw ('FAILED: '+$Name+' unexpectedly rejected: '+$_.Exception.Message)}
    $script:taskChecks++
}
function Expect-Reject([string]$Name,[scriptblock]$Action,[string]$ExpectedMessage){
    $caught=$null
    try{& $Action}catch{$caught=$_.Exception.Message}
    if($null -eq $caught){throw ('FAILED: '+$Name+' unexpectedly accepted.')}
    if($caught -notlike ('*'+$ExpectedMessage+'*')){
        throw ('FAILED: '+$Name+' rejected for the wrong reason: '+$caught)
    }
    $script:taskChecks++
}
function New-TestState([switch]$Legacy){
    $previous=@($rules | ForEach-Object {
        [pscustomobject]@{Name=$_.Name;Exists=$false;Kind=$_.Kind;Value=$null}
    })
    $applied=@($rules | ForEach-Object {
        [pscustomobject]@{Name=$_.Name;Exists=$true;Kind=$_.Kind;Value=$_.Value}
    })
    $state=[ordered]@{
        Schema=1;Sid=$sid;Computer=$env:COMPUTERNAME;RegistryPath=$policyPath
        Previous=$previous;Applied=$applied
    }
    if(-not $Legacy){$state.Preset=$Preset}
    # Use the same object shape produced by reading a JSON recovery record.
    return ($state | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
}

$taskOriginalComputer=$env:COMPUTERNAME
$taskTempParent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$taskTempPrefix=$taskTempParent.TrimEnd([char[]]@('\','/'))+[IO.Path]::DirectorySeparatorChar
$taskTestDirectory=Join-Path $taskTempParent ('EnvGuard-ChromePresetTests-'+[guid]::NewGuid().ToString('N'))
$taskTestDirectory=[IO.Path]::GetFullPath($taskTestDirectory)
if(-not $taskTestDirectory.StartsWith($taskTempPrefix,[StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($taskTestDirectory) -notmatch '^EnvGuard-ChromePresetTests-[a-f0-9]{32}$'){
    throw 'Unsafe test directory.'
}
try{
    [IO.Directory]::CreateDirectory($taskTestDirectory) | Out-Null
    $env:COMPUTERNAME='EnvGuard-Fixture-Computer'
    $sid='S-1-5-21-111-222-333-444'
    $policyPath='Software\EnvGuard\Fixtures\ChromePresetPolicy'
    $fullRules=@(
        [pscustomobject]@{Name='ProxySettings';Kind='String';Value='fixture-proxy-only'},
        [pscustomobject]@{Name='WebRtcIPHandling';Kind='String';Value='disable_non_proxied_udp'},
        [pscustomobject]@{Name='NetworkPredictionOptions';Kind='DWord';Value=2},
        [pscustomobject]@{Name='QuicAllowed';Kind='DWord';Value=0},
        [pscustomobject]@{Name='BackgroundModeEnabled';Kind='DWord';Value=0},
        [pscustomobject]@{Name='SyncDisabled';Kind='DWord';Value=1}
    )

    $Preset='Full';$rules=$fullRules
    $legacyFull=New-TestState -Legacy
    Expect-Accept 'legacy Full Schema 1 without Preset' {Validate-State $legacyFull}
    $validFull=New-TestState
    Expect-Accept 'explicit Full six-policy record' {Validate-State $validFull}
    $wrongFull=New-TestState;$wrongFull.Preset='Basic'
    Expect-Reject 'Full context rejects Basic marker' {Validate-State $wrongFull} 'Full'

    $Preset='Basic';$rules=@($fullRules | Where-Object Name -in @('WebRtcIPHandling','NetworkPredictionOptions'))
    $validBasic=New-TestState
    Expect-Accept 'Basic two-policy record' {Validate-State $validBasic}
    $legacyBasic=New-TestState -Legacy
    Expect-Reject 'Basic requires its own preset marker' {Validate-State $legacyBasic} 'Basic'
    $wrongBasic=New-TestState;$wrongBasic.Preset='Full'
    Expect-Reject 'Basic context rejects Full marker' {Validate-State $wrongBasic} 'Basic'
    Expect-Reject 'Basic context rejects legacy six-policy Full' {Validate-State $legacyFull} 'Basic'

    $foreignSid=New-TestState;$foreignSid.Sid='S-1-5-21-999-888-777-666'
    Expect-Reject 'foreign user record' {Validate-State $foreignSid} '当前设备/用户'
    $foreignComputer=New-TestState;$foreignComputer.Computer='Other-Fixture-Computer'
    Expect-Reject 'foreign computer record' {Validate-State $foreignComputer} '当前设备/用户'
    $foreignPath=New-TestState;$foreignPath.RegistryPath='Software\EnvGuard\Fixtures\OtherPolicy'
    Expect-Reject 'foreign registry namespace' {Validate-State $foreignPath} '当前设备/用户'
    $wrongSchema=New-TestState;$wrongSchema.Schema=2
    Expect-Reject 'unsupported state schema' {Validate-State $wrongSchema} '当前设备/用户'
    $missingRow=New-TestState;$missingRow.Applied=@($missingRow.Applied[0])
    Expect-Reject 'missing policy row' {Validate-State $missingRow} '不完整'
    $duplicateRow=New-TestState;$duplicateRow.Previous[1].Name=$duplicateRow.Previous[0].Name
    Expect-Reject 'duplicate policy name' {Validate-State $duplicateRow} '不完整'
    $unknownRow=New-TestState;$unknownRow.Applied[0].Name='UnrelatedPolicy'
    Expect-Reject 'unrelated policy name' {Validate-State $unknownRow} '格式异常'
    $wrongKind=New-TestState;$wrongKind.Applied[0].Kind='Binary'
    Expect-Reject 'unsupported registry value kind' {Validate-State $wrongKind} '格式异常'
    $wrongBoolean=New-TestState;$wrongBoolean.Applied[0].Exists='true'
    Expect-Reject 'non-Boolean Exists flag' {Validate-State $wrongBoolean} '格式异常'
    $wrongOrder=New-TestState;$wrongOrder.Applied=@($wrongOrder.Applied[1],$wrongOrder.Applied[0])
    Expect-Reject 'policy order mismatch' {Validate-State $wrongOrder} '顺序异常'

    $Preset='Full';$rules=$fullRules
    Expect-Reject 'Full context rejects Basic two-policy record' {Validate-State $validBasic} 'Full'
    $fullMarkedBasic=$validBasic | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $fullMarkedBasic.Preset='Full'
    Expect-Reject 'Full context rejects incomplete renamed Basic record' {Validate-State $fullMarkedBasic} '不完整'

    # Only synthetic state files inside the validated random temporary directory.
    $otherFull=Join-Path $taskTestDirectory 'synthetic-full-state.json'
    $otherBasic=Join-Path $taskTestDirectory 'synthetic-basic-state.json'
    $Preset='Basic'
    Expect-Accept 'Basic allows no active Full state' {Assert-NoOtherPreset $otherFull}
    [IO.File]::WriteAllText($otherFull,($validFull | ConvertTo-Json -Depth 8))
    Expect-Reject 'Basic rejects active Full state' {Assert-NoOtherPreset $otherFull} '另一套 Chrome 设置'
    $Preset='Full'
    Expect-Accept 'Full allows no active Basic state' {Assert-NoOtherPreset $otherBasic}
    [IO.File]::WriteAllText($otherBasic,($validBasic | ConvertTo-Json -Depth 8))
    Expect-Reject 'Full rejects active Basic state' {Assert-NoOtherPreset $otherBasic} '另一套 Chrome 设置'
    $unreadableState=Join-Path $taskTestDirectory 'synthetic-invalid-state.json'
    [IO.File]::WriteAllText($unreadableState,'not JSON')
    Expect-Reject 'existing unknown state is conservatively blocked' {Assert-NoOtherPreset $unreadableState} '另一套 Chrome 设置'

    Write-Output ('PASS: Chrome preset state validation / legacy Full compatibility / overlap rejection ('+$script:taskChecks+' checks); real Chrome and EnvGuard state unchanged')
}finally{
    $env:COMPUTERNAME=$taskOriginalComputer
    $taskResolvedCleanup=[IO.Path]::GetFullPath($taskTestDirectory)
    if(-not $taskResolvedCleanup.StartsWith($taskTempPrefix,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($taskResolvedCleanup) -notmatch '^EnvGuard-ChromePresetTests-[a-f0-9]{32}$'){
        throw 'Unsafe cleanup target; temporary fixtures preserved.'
    }
    if(Test-Path -LiteralPath $taskResolvedCleanup){Remove-Item -LiteralPath $taskResolvedCleanup -Recurse -Force}
}
