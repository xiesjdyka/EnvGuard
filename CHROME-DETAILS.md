# Chrome 策略详细说明

这里解释完整脚本的规则和限制。只想参考当时怎么操作，请先看 [白话版 Chrome 思路](CHROME-PRIVACY.md)。

本指南适用于 Claude 桌面版打开浏览器登录，以及你授权软件使用 Chrome 浏览网页的场景。**减少意外直连和多余数据暴露，不等于匿名、不等于所有流量隔离，也不保证账号不会被限制。**不要把这里的配置用于冒充所在地或绕过服务的资格要求。

顺序：**准备可靠网络 → 配置专用 Chrome → 验证策略和出口 → 保存 EnvGuard 基准 → 再登录或使用 Claude。**没有验证好就先不登录。

## 1. 准备专用浏览器配置

- 用正版 Chrome 并保持更新、安全浏览和证书校验开启。不要关闭沙箱、忽略 HTTPS 错误或安装来历不明的证书。
- 点击右上角头像，新建专用配置，例如“工作专用”，不导入个人浏览历史、密码和不需要的扩展。不必为了使用 Claude 而登录 Chrome 浏览器同步。多个配置能分开历史、密码、书签，但**不是网络隔离**。[Chrome 官方多配置说明](https://support.google.com/chrome/answer/2364824)
- 确认 Windows 默认浏览器和 Claude 实际弹出的登录浏览器是你准备的 Chrome。**默认浏览器是 Chrome，并不证明它一定打开专用配置**。先打开专用窗口；登录回调出现时再次检查头像和配置。必要时通过 Chrome“关于/版本”页的 `Profile Path` 确认，不要在错误配置里输入账号。
- 定位所用配置的 `Preferences` 文件，首次配置 EnvGuard 时选择它。不要一直选择 `Default` 而实际使用 `Profile 1`。
- 语言、日期和时区按你的真实工作需求设置。保留自动计时和正常夏令时，不锁死时间，不依赖改语言、字体或 Emoji 来“证明账号安全”。

## 2. 可选：应用浏览器网络/隐私策略

包内的 `ChromePrivacy.ps1` **默认仅检查，绝不自动修改**。下面讲的是完整六项模式（Full）；只想参考昨天的两项设置，用 [简单操作里的 Basic 模式](CHROME-PRIVACY.md)。执行 `Apply` 并明确确认作用范围后才修改。两种模式不能同时应用，切换前须先恢复当前模式。

**重要：当前用户注册表策略会影响该 Windows 用户的所有 Chrome 配置，不只专用窗口。**不适合与需要直连、同步、视频通话的日常 Chrome 混用。浏览器设置和手机/其他用户不在此脚本覆盖范围。

在解压后的文件夹打开 PowerShell 7。先检查：

```powershell
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode Inspect -Preset Full
```

先确保本机代理已正常运行，确认 HTTP/mixed 端口，再应用。例如本机端口 `10808`：

```powershell
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode Apply -Preset Full -ProxyPort 10808 -ConfirmAllChromeProfiles
```

端口要填本机软件的 HTTP/mixed 监听端口，**不是卖家给的远程节点端口**。若权限被拒绝，使用同一 Windows 账号以管理员方式打开 PowerShell 7，重新检查；不要修改注册表权限或改用另一账号猜测操作。

| 策略 | 设置与用途 |
| --- | --- |
| `ProxySettings` | 固定到 `http://127.0.0.1:你的端口`，不添加 `DIRECT` 回退；只明确放行本机登录回调地址，不放行公网域名 |
| `WebRtcIPHandling` | `disable_non_proxied_udp`，限制不经过代理的 WebRTC UDP；可能影响语音、视频或实时通信 |
| `NetworkPredictionOptions` | `2`，关闭 DNS 预取、连接预测和页面预加载 |
| `QuicAllowed` | `0`，禁用 Chrome QUIC，需要完整重启浏览器 |
| `BackgroundModeEnabled` | `0`，关闭“关窗口后继续运行后台应用”的模式；不代表所有 Chrome 进程一定立即退出 |
| `SyncDisabled` | `1`，禁用 Chrome 的 Google 云端同步；**不是禁止登录网站或禁止 Google OAuth** |

策略来自 [Chromium 官方 ProxySettings 定义](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/Miscellaneous/ProxySettings.yaml)、[WebRTC 定义](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/WebRtc/WebRtcIPHandling.yaml)、[预加载定义](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/Miscellaneous/NetworkPredictionOptions.yaml)、[QUIC 定义](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/Miscellaneous/QuicAllowed.yaml)、[后台模式定义](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/Miscellaneous/BackgroundModeEnabled.yaml) 和 [同步定义](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/Miscellaneous/SyncDisabled.yaml)。

固定代理适用于 Chrome 代理机制覆盖的 HTTP/HTTPS/WebSocket 请求；单一代理不可用时，这些请求通常报代理连接错误，而不是因为本脚本提供了直连备选。**代理软件自己仍可能按其路由规则直连，因此本机代理端口通了不等于远程出口正确。**脚本不会替你修改 v2rayN 的实际路由，也不对 Claude 自身、扩展的外部程序、系统 DNS、其他软件或未知协议做整机拦截。[Chromium 官方代理行为说明](https://github.com/chromium/chromium/blob/main/net/docs/proxy.md)

本机例外保留 `localhost` / `*.localhost`、IPv4 `127.0.0.0/8` 和 IPv6 `[::1]`，便于桌面软件登录回调，不是给登录官网设置直连。不要添加 `<local>`、单独的 `*` 或公网域名绕过项。此包不会把这些本地回调送到远程代理。

## 3. 重启、验证，再记录基准

1. 保存正在编辑的内容，完整退出 Chrome 所有窗口和后台进程，再打开专用配置。脚本不会自行杀浏览器。
2. 打开 `chrome://policy`，重新加载策略。核对上表六项的值、来源和状态是否正常。注册表写入成功≠Chrome 已加载成功；机器/云端管理、过期策略或策略冲突都可能影响实际生效。
3. `ProxySettings` 展开查看：确认 `fixed_servers`、正确本机端口、没有 `direct://`、没有公网绕过域名。发现冲突先暂停；不要删除公司/组织管理策略强行绕过。
4. 在专用 Chrome 查公网出口，与 EnvGuard 两个出口检查的结果对照。浏览器出口检查提供单次证据，不是全部流量证明。
5. 可用一个不涉及真实账号的网页复核 WebRTC 可见地址，避免出现不应暴露的公网地址。第三方检测站可能误判字体或其他指纹，不能据此宣布“完全安全”。不要上传真实登录回调地址或 Cookie 给检测站。
6. EnvGuard 选择这个 Chrome 的 `Preferences`，再生成快照、核对并保存。策略或浏览器配置后来变化会与基准比较；改好再保存，别先保存错误环境。

**不要为测试而在 Claude 登录中途断开代理。**代理断开试验仅在无登录、无工作、无敏感页面时进行，结束后重新确认实际出口。EnvGuard 只是预警，不会自动关闭浏览器；检测有时限。

## 4. 每次登录/调用浏览器前

- 先开 EnvGuard，确认当前所选检查项通过；代理出口、时区和浏览器配置均已核对。
- 确认登录页网址和 HTTPS 证书正常，再输入账号。只有可信官方网站才能接收你的密码、验证码和授权。
- 定位、摄像头、麦克风、通知和剪贴板读取，按网站实际必要性授权；不需要就拒绝。不要“一键允许所有站点”。
- 保持浏览器扩展最少。能读取所有网页的扩展可能接触登录页和会话数据；不要为了一个字体风险分装许多指纹修改扩展。已有字体防护扩展不保证隐藏全部系统字体。
- 不公开截图中的节点链接、UUID、Cookie、浏览器配置文件或登录回调 URL。独享 IP 不代表独享线路稳定，也不代表网络身份不可见。
- 网站、代理服务和 OAuth 提供方仍能知道该次连接的出口及授权信息，正常登录不可能对目标网站“什么都不透露”。

## 5. 异常时怎么处理，以及 Chrome 全进程范围

遇到环境告警，先停止新的登录和浏览器操作，查看原因。需要停止就点红色紧急按钮。软件控制浏览器时，不要以为关掉 Claude 主窗口就等于关掉 Chrome 或全部外部任务。

若要一起关闭 Chrome：首次配置里**另外添加 `chrome.exe`**，核对它的范围。**相同安装路径的 Chrome 通常共享一套程序身份；紧急关闭可能结束该安装的所有 Chrome 配置窗口和后台进程，无法仅凭 EXE 区分专用配置。**在用其他 Chrome 窗口工作的用户务必理解影响；本工具不会自动把 Chrome 加入关闭列表。

若自动化打开的是其他浏览器、不同 Chrome 安装、独立运行时或远程任务，也要单独确认保护范围。没有配置或不能可靠识别的进程，不会被假装已覆盖。若需要“每个请求都拦截、任何绕过都禁止”，应采用专门的网络隔离/防火墙架构，不能把本指南加预警当成等价方案。

## 6. 恢复原策略

脚本只为自己管理的六项保存恢复记录，不备份浏览历史、密码或整个浏览器。记录放在 `%LOCALAPPDATA%\EnvGuard\chrome-policy-state.json`，**不要公开上传**。重复应用不会丢掉第一次的原值；遇到其他操作改了策略会拒绝覆盖。

```powershell
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode Restore -Preset Full -ConfirmAllChromeProfiles
```

恢复你应用前的六项值，不删除其他策略。恢复后重启 Chrome；原代理路径可能是系统代理或直连，所以先暂停敏感网页操作，再重新确认环境。不会改系统网络，其他软件恢复正常网络由你自己控制。

写策略前记录请求，完成后记录结果，默认沿用现有 EnvGuard 的日志文件；首次配置前则写 `%LOCALAPPDATA%\EnvGuard\logs\events.jsonl`。也可通过 `-LogFile 'D:\你的日志目录\events.jsonl'` 指定。日志请求无法保存时不会开始策略写入；硬盘或权限故障仍可能让后续结果记录失败，脚本会报错而不是伪造成功。

脚本自测只操作随机命名的隔离测试注册表项，不修改真实 Chrome：

```powershell
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode SelfTest
```
