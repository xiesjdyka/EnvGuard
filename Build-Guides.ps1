param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
# Offline documentation: no CDN, telemetry, scripts or automatically loaded images.
# Source is the same Markdown reviewed on GitHub; escape everything except supported formatting.
function Encode([string]$Value){[Net.WebUtility]::HtmlEncode($Value)}
function Guide-Link([string]$Url){
    if($Url -eq 'README.md'){return '先看这里.html'}
    if($Url -eq 'CHROME-PRIVACY.md'){return 'Chrome浏览器怎么准备.html'}
    if($Url -match '^https://'){return $Url}
    if($Url -match '^[A-Za-z0-9._/#-]+$'){return 'https://github.com/xiesjdyka/EnvGuard/blob/main/'+$Url}
    return '#'
}
function Inline([string]$Value){
    $encoded=Encode $Value
    $encoded=[regex]::Replace($encoded,'`([^`]+)`', '<code>$1</code>')
    $encoded=[regex]::Replace($encoded,'\*\*([^*]+)\*\*','<strong>$1</strong>')
    [regex]::Replace($encoded,'\[([^\[\]]+)\]\(([^\s)]+)\)',{param($m) '<a href="'+(Encode (Guide-Link ([Net.WebUtility]::HtmlDecode($m.Groups[2].Value))))+'">'+$m.Groups[1].Value+'</a>'})
}
function Body([string]$Source){
    $lines=$Source -split '\r?\n';$result=[Collections.Generic.List[string]]::new();$list='';$fence=$false;$table=$false
    for($i=0;$i -lt $lines.Count;$i++){
        $line=$lines[$i]
        if($line -match '^```'){
            if($list){$result.Add('</'+$list+'>');$list=''}
            if($fence){$result.Add('</code></pre>');$fence=$false}else{$result.Add('<pre tabindex="0"><code>');$fence=$true};continue
        }
        if($fence){$result.Add((Encode $line));continue}
        if($table -and $line -notmatch '^\|'){$result.Add('</tbody></table></div>');$table=$false}
        if($line -match '^\|'){
            if($line -match '^\|[\s:|-]+\|$'){continue}
            $cells=@($line.Trim().Trim('|') -split '\|')
            if(-not $table){$result.Add('<div class="table-wrap"><table><thead><tr>');foreach($cell in $cells){$result.Add('<th>'+(Inline $cell.Trim())+'</th>')};$result.Add('</tr></thead><tbody>');$table=$true}
            else{$result.Add('<tr>');foreach($cell in $cells){$result.Add('<td>'+(Inline $cell.Trim())+'</td>')};$result.Add('</tr>')};continue
        }
        $kind='';$text='';$start=1
        if($line -match '^\s*[-*] (.+)$'){$kind='ul';$text=$Matches[1]}
        elseif($line -match '^\s*(\d+)\. (.+)$'){$kind='ol';$start=[int]$Matches[1];$text=$Matches[2]}
        if($list -and $kind -ne $list){$result.Add('</'+$list+'>');$list=''}
        if($kind){if(-not $list){$result.Add('<'+$kind+$(if($kind -eq 'ol'){' start="'+$start+'"'}else{''})+'>');$list=$kind};$result.Add('<li>'+(Inline $text)+'</li>');continue}
        if([string]::IsNullOrWhiteSpace($line)){continue}
        if($line -eq '<details>' -or $line -eq '</details>'){$result.Add($line);continue}
        if($line -match '^<summary>(.*)</summary>$'){$result.Add('<summary>'+(Inline $Matches[1])+'</summary>');continue}
        if($line -match '^(#{1,6}) (.+)$'){$level=$Matches[1].Length;$result.Add('<h'+$level+'>'+(Inline $Matches[2])+'</h'+$level+'>');continue}
        if($line -match '^\[!\['){$result.Add('<p><a href="https://github.com/xiesjdyka/EnvGuard/blob/main/media/EnvGuard-Claude.mp4">在 GitHub 看演示动画</a></p>');continue}
        if($line -match '^> (.*)$'){$result.Add('<p class="note">'+(Inline $Matches[1])+'</p>');continue}
        $result.Add('<p>'+(Inline $line)+'</p>')
    }
    if($list){$result.Add('</'+$list+'>')};if($table){$result.Add('</tbody></table></div>')};if($fence){throw '说明中代码段未闭合'}
    return $result -join "`n"
}
$style=@'
:root{--ink:#1e2b3c;--muted:#526273;--blue:#184e8b;--border:#d7dfe8;--surface:#f4f7fa}
*{box-sizing:border-box}body{margin:0;background:var(--surface);color:var(--ink);font:16px/1.8 "Segoe UI","Microsoft YaHei UI",sans-serif}a{color:var(--blue);text-underline-offset:4px}a:hover{text-decoration-thickness:2px}a:focus-visible,summary:focus-visible,pre:focus-visible{outline:3px solid var(--blue);outline-offset:4px}nav{max-width:960px;margin:auto;padding:24px 32px;display:flex;gap:24px;flex-wrap:wrap}nav .brand{font-weight:700;margin-right:auto}main{max-width:960px;margin:0 auto 48px;padding:32px 48px 48px;background:white;border:1px solid var(--border);border-radius:12px}h1{font-size:30px;line-height:1.45;margin:0 0 24px;max-width:25em}h2{font-size:23px;margin:40px 0 16px;padding-top:12px;border-top:1px solid var(--border)}h3{font-size:19px;margin:24px 0 12px}p{margin:12px 0}li{margin:8px 0}ul,ol{padding-left:26px}strong{font-weight:650}code{font:0.92em/1.6 Consolas,monospace;background:var(--surface);border-radius:4px;padding:2px 5px;overflow-wrap:anywhere}pre{border:1px solid var(--border);background:var(--surface);padding:16px 20px;border-radius:8px;white-space:pre-wrap;overflow-wrap:anywhere;margin:16px 0}pre code{padding:0;background:none}details{background:var(--surface);padding:16px 20px;border-radius:8px;margin:20px 0}summary{cursor:pointer;font-weight:650}summary~*{margin-top:16px}.note{border-left:3px solid var(--blue);padding:12px 20px;background:var(--surface)}table{width:100%;border-collapse:collapse;margin:16px 0;font-size:15px}th,td{padding:12px;text-align:left;border:1px solid var(--border);vertical-align:top}th{background:var(--surface)}.table-wrap{overflow-x:auto}footer{max-width:960px;margin:0 auto 32px;padding:0 32px;color:var(--muted);font-size:14px}@media(max-width:600px){nav{padding:16px;gap:16px}main{margin:0 12px 32px;padding:24px 20px}h1{font-size:25px}h2{font-size:21px}footer{padding:0 20px}}
'@
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
foreach($entry in @(@{Source='README.md';File='先看这里.html';Title='EnvGuard · 从这里开始'},@{Source='CHROME-PRIVACY.md';File='Chrome浏览器怎么准备.html';Title='Chrome · 操作与核对'})){
    $body=Body (Get-Content -LiteralPath (Join-Path $PSScriptRoot $entry.Source) -Raw)
    $html='<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src ''none''; style-src ''unsafe-inline''; base-uri ''none''; form-action ''none''"><title>'+(Encode $entry.Title)+'</title><style>'+$style+'</style></head><body><nav aria-label="说明导航"><span class="brand">EnvGuard</span><a href="先看这里.html">使用说明</a><a href="Chrome浏览器怎么准备.html">Chrome 方法</a><a href="https://github.com/xiesjdyka/EnvGuard/discussions">提建议</a></nav><main id="content">'+$body+'</main><footer>这份说明可以离线阅读。代码框里的命令需要你复制后执行；打开说明不会修改电脑。</footer></body></html>'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $entry.File),$html,[Text.UTF8Encoding]::new($false))
}
