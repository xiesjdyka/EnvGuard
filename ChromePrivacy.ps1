param(
    [ValidateSet('Inspect','Apply','Restore','SelfTest')][string]$Mode='Inspect',
    [ValidateRange(1,65535)][int]$ProxyPort=10808,
    [switch]$ConfirmAllChromeProfiles,
    [string]$LogFile
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if($env:OS -ne 'Windows_NT'){throw '此脚本只支持 Windows，推荐 PowerShell 7。'}
$policyPath='Software\Policies\Google\Chrome'
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$stateFile=Join-Path $env:LOCALAPPDATA 'EnvGuard\chrome-policy-state.json'
$rules=@(
    [pscustomobject]@{Name='ProxySettings';Kind='String';Value=(@{ProxyMode='fixed_servers';ProxyServer="http://127.0.0.1:$ProxyPort";ProxyBypassList='<-loopback>;localhost;*.localhost;127.0.0.0/8;[::1]'} | ConvertTo-Json -Compress)},
    [pscustomobject]@{Name='WebRtcIPHandling';Kind='String';Value='disable_non_proxied_udp'},
    [pscustomobject]@{Name='NetworkPredictionOptions';Kind='DWord';Value=2},
    [pscustomobject]@{Name='QuicAllowed';Kind='DWord';Value=0},
    [pscustomobject]@{Name='BackgroundModeEnabled';Kind='DWord';Value=0},
    [pscustomobject]@{Name='SyncDisabled';Kind='DWord';Value=1}
)
function Read-Policies([string]$Path,[Microsoft.Win32.RegistryHive]$Hive=[Microsoft.Win32.RegistryHive]::CurrentUser){
    $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive,[Microsoft.Win32.RegistryView]::Registry64)
    $key=$root.OpenSubKey($Path)
    try{foreach($rule in $rules){
        if($key -and $key.GetSubKeyNames() -contains $rule.Name){throw ('策略使用子键格式，本脚本拒绝猜测/覆盖：'+$rule.Name+'；请查看 chrome://policy')}
        $exists=$null -ne $key -and $key.GetValueNames() -contains $rule.Name
        if($exists){$kind=$key.GetValueKind($rule.Name).ToString();if($kind -notin @('String','DWord')){throw ('策略类型异常，拒绝覆盖：'+$rule.Name)};$value=$key.GetValue($rule.Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$kind=$rule.Kind;$value=$null}
        [pscustomobject]@{Name=$rule.Name;Exists=$exists;Kind=$kind;Value=$value}
    }}finally{if($key){$key.Dispose()};$root.Dispose()}
}
function Write-Policies([string]$Path,[object[]]$Rows){
    $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
    $key=$root.CreateSubKey($Path,$true)
    try{foreach($row in $Rows){if($row.Name -notin $rules.Name){throw '未知策略名，拒绝写入'};if($row.Exists){$kind=[Microsoft.Win32.RegistryValueKind]([Enum]::Parse([Microsoft.Win32.RegistryValueKind],$row.Kind));$value=if($kind -eq [Microsoft.Win32.RegistryValueKind]::DWord){[int]$row.Value}else{[string]$row.Value};$key.SetValue($row.Name,$value,$kind)}else{$key.DeleteValue($row.Name,$false)}};$key.Flush()}finally{$key.Dispose();$root.Dispose()}
}
function Same-Policy($Left,$Right){return $Left.Name -eq $Right.Name -and $Left.Exists -eq $Right.Exists -and (-not $Left.Exists -or ($Left.Kind -eq $Right.Kind -and [string]$Left.Value -ceq [string]$Right.Value))}
function Validate-State($State){
    if($State.Schema -ne 1 -or $State.Sid -ne $sid -or $State.Computer -ne $env:COMPUTERNAME -or $State.RegistryPath -ne $policyPath){throw '还原记录不属于当前设备/用户，拒绝使用'}
    foreach($rows in @($State.Previous,$State.Applied)){if($rows.Count -ne $rules.Count -or (@($rows.Name | Sort-Object -Unique).Count -ne $rules.Count)){throw '还原记录不完整'};foreach($row in $rows){if($row.Name -notin $rules.Name -or $row.Kind -notin @('String','DWord') -or $row.Exists -isnot [bool]){throw '还原记录格式异常'}}}
    for($i=0;$i -lt $rules.Count;$i++){if($State.Previous[$i].Name -ne $rules[$i].Name -or $State.Applied[$i].Name -ne $rules[$i].Name){throw '还原记录顺序异常'}}
}
function Save-State($State){
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($stateFile)) | Out-Null
    $tempFile=$stateFile+'.pending'
    [IO.File]::WriteAllText($tempFile,($State | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    if([IO.File]::Exists($stateFile)){[IO.File]::Replace($tempFile,$stateFile,$null)}else{[IO.File]::Move($tempFile,$stateFile)}
}
function Audit-Change([string]$Event,$Data){
    if([string]::IsNullOrWhiteSpace($LogFile)){throw '日志路径为空'}
    $absolute=[IO.Path]::GetFullPath($LogFile);if(-not $absolute.EndsWith('.jsonl',[StringComparison]::OrdinalIgnoreCase)){throw '日志文件应为 .jsonl'}
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($absolute)) | Out-Null
    $line=@{schema=1;time=[DateTimeOffset]::Now.ToString('o');utc=[DateTime]::UtcNow.ToString('o');kind=$Event;data=$Data} | ConvertTo-Json -Depth 10 -Compress
    $bytes=[Text.Encoding]::UTF8.GetBytes($line+"`r`n")
    for($attempt=0;;$attempt++){try{$stream=[IO.FileStream]::new($absolute,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::Read,4096,[IO.FileOptions]::WriteThrough);try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()};break}catch [IO.IOException]{if($attempt -ge 4){throw};Start-Sleep -Milliseconds 25}}
}
if($Mode -eq 'SelfTest'){
    # Isolated test namespace only: no writes to real Chrome policy or EnvGuard profile.
    $testPath='Software\EnvGuard\SelfTest\'+[guid]::NewGuid().ToString('N')
    try{$before=@(Read-Policies $testPath);$applied=@($rules | ForEach-Object {[pscustomobject]@{Name=$_.Name;Exists=$true;Kind=$_.Kind;Value=$_.Value}});Write-Policies $testPath $applied;$after=@(Read-Policies $testPath)
        for($i=0;$i -lt $rules.Count;$i++){if(-not (Same-Policy $after[$i] $applied[$i])){throw '测试失败：策略写入/读取'}}
        $proxy=$after[0].Value | ConvertFrom-Json;if($proxy.ProxyMode -ne 'fixed_servers' -or $proxy.ProxyServer -match 'direct' -or $proxy.ProxyBypassList -ne '<-loopback>;localhost;*.localhost;127.0.0.0/8;[::1]'){throw '测试失败：代理约束'}
        Write-Policies $testPath $before;$restored=@(Read-Policies $testPath);if(@($restored | Where-Object Exists).Count -ne 0){throw '测试失败：策略还原'}
        Write-Output 'PASS: Chrome helper isolated registry roundtrip / loopback-only bypass / restore (8 checks); real Chrome unchanged'
    }finally{if($testPath -notmatch '^Software\\EnvGuard\\SelfTest\\[a-f0-9]{32}$'){throw '测试清理路径不安全'};[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testPath,$false)}
    return
}
$scriptMutex=[Threading.Mutex]::new($false,('Local\EnvGuard-ChromePrivacy-'+$sid))
if(-not $scriptMutex.WaitOne(0)){$scriptMutex.Dispose();throw 'Chrome 配置脚本已在运行；请等它结束再重试'}
try{
$current=@(Read-Policies $policyPath)
$machine=@(Read-Policies $policyPath ([Microsoft.Win32.RegistryHive]::LocalMachine))
if($Mode -eq 'Inspect'){
    Write-Output '只读检查；以 chrome://policy 的实际生效状态为准。HKLM/云端策略可能覆盖当前用户设置。'
    $current | Format-Table Name,Exists,Kind,Value -Wrap
    if(@($machine | Where-Object Exists).Count -gt 0){Write-Output '发现机器级策略：';$machine | Where-Object Exists | Format-Table Name,Kind,Value -Wrap}
    return
}
if(-not $ConfirmAllChromeProfiles){throw '操作会影响当前用户所有 Chrome 配置文件。确认了解影响后增加 -ConfirmAllChromeProfiles。'}
if([string]::IsNullOrWhiteSpace($LogFile)){
    $profileFile=Join-Path $env:LOCALAPPDATA 'EnvGuard\profiles\default.json'
    if(Test-Path -LiteralPath $profileFile){$profile=Get-Content -LiteralPath $profileFile -Raw | ConvertFrom-Json;if($profile.UserSid -ne $sid -or $profile.Computer -ne $env:COMPUTERNAME){throw '本地配置来自其他设备/用户'};$LogFile=[string]$profile.LogFile}else{$LogFile=Join-Path $env:LOCALAPPDATA 'EnvGuard\logs\events.jsonl'}
}
$state=$null
if(Test-Path -LiteralPath $stateFile){$state=Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json;Validate-State $state}
if($Mode -eq 'Restore'){
    if($null -eq $state){throw '没有本工具保存的策略还原记录；不会删除或猜测其他设置'}
    for($i=0;$i -lt $rules.Count;$i++){if(-not (Same-Policy $current[$i] $state.Applied[$i]) -and -not (Same-Policy $current[$i] $state.Previous[$i])){throw ('策略已被其他操作改变，拒绝覆盖：'+$current[$i].Name)}}
    Audit-Change 'chrome_policy_restore_requested' @{names=$rules.Name}
    Write-Policies $policyPath @($state.Previous)
    $verified=@(Read-Policies $policyPath);for($i=0;$i -lt $rules.Count;$i++){if(-not (Same-Policy $verified[$i] $state.Previous[$i])){throw '恢复后校验未通过，请 Inspect 核对；保留恢复记录'}}
    Audit-Change 'chrome_policy_restored' @{names=$rules.Name}
    Move-Item -LiteralPath $stateFile -Destination ($stateFile+'.restored-'+[guid]::NewGuid().ToString('N'))
    Write-Output '已恢复本工具修改前的六项当前用户策略；未改动其他策略。请重启 Chrome，再重新核对 EnvGuard 基准。'
    return
}
if(@($machine | Where-Object Exists).Count -gt 0){throw '存在相关机器级策略，拒绝悄悄覆盖；请在 chrome://policy 检查优先级或联系设备管理员'}
foreach($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryHive]::LocalMachine)){
    $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,[Microsoft.Win32.RegistryView]::Registry64);$key=$root.OpenSubKey($policyPath)
    try{if($key){foreach($name in @('WebRtcIPHandlingUrl','ProxyOverrideRules')){if($key.GetValueNames() -contains $name -or $key.GetSubKeyNames() -contains $name){throw ('存在单独的网址覆盖策略，无法保证六项策略效果，请先核对：'+$name)}};if($hive -eq [Microsoft.Win32.RegistryHive]::LocalMachine){foreach($name in @('ProxyMode','ProxyServer','ProxyBypassList','ProxyPacUrl')){if($key.GetValueNames() -contains $name){throw ('存在机器级代理策略，拒绝悄悄覆盖：'+$name)}}};if($key.GetValue('RoamingProfileSupportEnabled',0) -eq 1){throw '漫游配置支持已启用，与强制关闭同步策略有冲突，拒绝修改'};foreach($rule in $rules){if($key.GetSubKeyNames() -contains $rule.Name){throw ('现有策略使用子键格式，拒绝覆盖：'+$rule.Name)}}}}finally{if($key){$key.Dispose()};$root.Dispose()}
}
if($state){for($i=0;$i -lt $rules.Count;$i++){if(-not (Same-Policy $current[$i] $state.Applied[$i])){throw '当前策略与上次应用不同；先 Inspect 核对，不覆盖第三方改动'}}}
$applied=@($rules | ForEach-Object {[pscustomobject]@{Name=$_.Name;Exists=$true;Kind=$_.Kind;Value=$_.Value}})
$previous=if($state){@($state.Previous)}else{$current}
$newState=@{Schema=1;Sid=$sid;Computer=$env:COMPUTERNAME;RegistryPath=$policyPath;Previous=$previous;Applied=$applied}
Audit-Change 'chrome_policy_apply_requested' @{names=$rules.Name;proxyPort=$ProxyPort}
Save-State $newState
try{Write-Policies $policyPath $applied;$verified=@(Read-Policies $policyPath);for($i=0;$i -lt $rules.Count;$i++){if(-not (Same-Policy $verified[$i] $applied[$i])){throw '策略写入后校验失败'}}}catch{try{Write-Policies $policyPath $current;if($state){Save-State $state};Audit-Change 'chrome_policy_apply_failed' @{error=$_.Exception.Message;rollback='previous attempt values restored'}}catch{Write-Warning '自动回滚未完全确认，请 Inspect 并查看还原记录'};throw}
Audit-Change 'chrome_policy_applied' @{names=$rules.Name;proxyPort=$ProxyPort}
Write-Output '六项策略已写入当前用户。请完整退出并重启 Chrome，在 chrome://policy 检查实际状态，再确认出口并保存 EnvGuard 基准。注册表写入成功不等于 Chrome 已生效。'
}finally{$scriptMutex.ReleaseMutex();$scriptMutex.Dispose()}
