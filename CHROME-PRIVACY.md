# 当时 Chrome 是怎么弄的？可以参考这条思路

使用 Claude 桌面版时，登录可能跳到浏览器；工作中也可能用浏览器打开网页。所以除了软件本身，也要看看浏览器有没有多余的连接或不必要的权限。

**这是一份独立的参考方法，不是要给 EnvGuard 再加浏览器功能。** 可以按自己的需要选用。目标是减少意外暴露，不是让网站完全不知道你是谁，也不保证账号不被限制。

## 当时先做了什么？

先把 Chrome 设为默认浏览器，然后处理两项网络设置：

- **限制 WebRTC 不经过代理的 UDP 连接。** WebRTC 常用于语音、视频等实时通信，它不一定和普通网页走完全一样的连接方式。
- **关闭提前加载和连接预测。** 不让 Chrome 为了“打开更快”，提前解析或连接你还没打开的内容。

当时在 `chrome://policy` 里看到这两项状态是“正常”，才确认 Chrome 已识别设置：

| 名字，不用背，核对时找它 | 当时的值 |
| --- | --- |
| `WebRtcIPHandling` | `disable_non_proxied_udp` |
| `NetworkPredictionOptions` | `2` |

这两项的作用可以查 [Chrome 的 WebRTC 定义](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/WebRtc/WebRtcIPHandling.yaml) 和 [提前加载定义](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/Miscellaneous/NetworkPredictionOptions.yaml)。

后面还核对了语言、时区等设置。但这些只是浏览器显示和工作环境的设置，**改成英文、换字体，不会让网络自动更安全**。尤其“默认字体改成 Arial”和“网站检测不到中文字体”是两回事。不要为了把第三方检测分数刷到零，反复装各种改指纹扩展。

## 想参考的话，先照这个顺序来

1. **先连好代理，再检查出口。** 在准备使用的 Chrome 窗口查一下公网 IP，确认是你想用的出口。代理连上不代表出口一定正确。
2. **尽量用一个工作专用的 Chrome 配置。** 点右上角头像新建配置，少装扩展，不导入不需要的密码和历史。它能分开这些浏览数据，但不等于网络隔离。设了默认浏览器后，也要看登录页实际打开的是不是这个配置。
3. **设置后，要去浏览器里确认。** 写入设置成功，不等于 Chrome 已经用上了。下面会说怎么看。
4. **最后再保存 EnvGuard 的设置。** 不要先把错误出口或没生效的设置记成正常。每次登录前先开 EnvGuard 看当前结果。

## 有代码吗？有，作为可选方案附在这里

包里的 [ChromePrivacy.ps1](ChromePrivacy.ps1) 可以直接修改相关设置，也支持恢复。**它比当时最初的两项设置多：还会固定 Chrome 代理、关闭 QUIC、关窗后台模式和浏览器同步。不是当时两项代码的原样复刻。** 完整规则见 [详细说明](CHROME-DETAILS.md)。

执行前先看清影响：

- 它会影响**当前 Windows 用户的全部 Chrome 配置**，不只是工作窗口。如果日常 Chrome 还要直连或同步，不要直接照搬。
- Chrome 会固定使用你填的本机代理；代理不可用时，普通网页请求可能打不开。
- 语音、视频等功能可能受影响；Chrome 的 Google 浏览器同步会被关闭。关闭同步不是禁止登录网站。
- 它不修改系统代理，不替你调整 v2rayN 路由，也不自动关浏览器。

### 怎么执行？

先从 [发布页](https://github.com/xiesjdyka/EnvGuard/releases/latest) 下载 ZIP 并解压。只看 GitHub 网页是不够的，脚本要在电脑上运行。

打开解压后的文件夹，在文件夹空白处右键，选择“在终端中打开”。使用 PowerShell 7；下面的命令一次复制一行，粘贴后按回车。

**先看看现有设置，不会修改：**

```powershell
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode Inspect
```

**确认了解上面的影响后，再应用：** 假设 v2rayN 底部显示的本机 `mixed` 端口是 `10808`：

```powershell
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode Apply -ProxyPort 10808 -ConfirmAllChromeProfiles
```

如果你显示的是别的本机端口，就只把 `10808` 换成自己的。**不是卖家给你的远程节点端口。**

如果提示找不到 `pwsh`，说明当前终端没有找到 PowerShell 7，请先确认它已安装。如果提示找不到脚本，说明终端没在解压后的文件夹里。若提示“拒绝访问”，用同一个 Windows 账号以管理员身份打开 PowerShell 7，再进入这个文件夹操作。

如果提示组织策略冲突或脚本被安全策略阻止，先停止操作，检查原因；不要删除组织策略、修改注册表权限或关闭安全防护来强行运行。

### 怎么看有没有生效？

1. 先保存浏览器里正在做的事，完整退出 Chrome，再重新打开。必要时确认后台也已退出；脚本不会帮你杀掉浏览器。
2. 在 Chrome 地址栏输入 `chrome://policy`，按回车，点“重新加载政策”。
3. 最初那两项应是上表的值。使用完整脚本后还应有 `ProxySettings`、`QuicAllowed`、`BackgroundModeEnabled` 和 `SyncDisabled`，这六项的状态都应正常。
4. 展开 `ProxySettings`，确认代理地址是 `http://127.0.0.1:你的本机端口`，模式是 `fixed_servers`。详细核对值见 [六项设置表](CHROME-DETAILS.md#2-可选应用浏览器网络隐私策略)。
5. 再在这个 Chrome 窗口查公网 IP，和 EnvGuard 检测到的出口对照。

没有出现、出现错误或出口不对，先不要继续登录。单次查 IP 通过，也不代表所有连接都验证过了。

`localhost` / `127.0.0.1` 这些本机地址是有意保留的，桌面软件登录回调可能需要它们，不是把登录官网放行直连。别自行加公网网站绕过项。

### 不想用了，怎么恢复？

先暂停敏感页面操作，在同一个文件夹执行：

```powershell
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode Restore -ConfirmAllChromeProfiles
```

它恢复的是**本脚本应用前保存的六项设置**，不是把所有 Chrome 设置清空，也不能撤销你以前用别的方法做过的修改。恢复后重开 Chrome，再查实际出口；原设置可能允许直连。

应用和恢复也会留日志：已有 EnvGuard 配置时使用你选的日志文件，否则默认写到 `%LOCALAPPDATA%\EnvGuard\logs\events.jsonl`。不要把本机的恢复记录和日志上传到公开仓库。

## 登录或让软件使用浏览器时，再注意这几件事

- 确认确实是官方网站，HTTPS 没有证书错误。不要把密码、验证码或登录回调链接交给第三方检测网站。
- 定位、摄像头、麦克风、通知等权限，不需要就拒绝，不要一口气全允许。
- 少装扩展，特别是能读取所有网页的扩展。改指纹扩展不是通用的防泄露办法。
- Chrome 和系统保持更新，不关闭安全浏览、沙箱或证书校验。
- 出口或设置异常时，先暂停操作。如果要连浏览器一起紧急关闭，必须把 Chrome 另外加入 EnvGuard 的保护软件；这可能关掉同一安装下的全部 Chrome 窗口。

## 它能防什么，不能防什么？

这条思路主要减少 Chrome 的额外连接和不必要的数据暴露。完整脚本给 Chrome 指定代理，但**代理软件自己仍可能按路由直连**；Claude 自身、其他软件、扩展调用的外部程序也不因此被隔离。[Chrome 官方代理说明](https://github.com/chromium/chromium/blob/main/net/docs/proxy.md)

登录的网站仍会知道你的账号和本次连接的出口。EnvGuard 的提醒也有检测时间，不是掉线瞬间就能拦住每个请求。

所以重点不是“把检测网站变成全绿”，而是：**知道连接怎么走，确认设置真的生效，少给不必要的权限，异常时停止操作。** 遵守所用服务的使用规则，不把这套方法当作冒充所在地或账号安全的保证。

[回到 EnvGuard 首页](README.md) · [查看完整 Chrome 技术说明](CHROME-DETAILS.md)
