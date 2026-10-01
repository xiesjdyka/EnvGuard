# Chrome 准备：执行代码 → 看结果 → 改语言

这就是当时的操作流程，作为独立参考放在这里。先连好代理；已经设置好、检查也正常的步骤不用重复做。EnvGuard 不会自动帮你执行这些操作。

## 第一步：执行代码

1. 下载并解压 [EnvGuard 程序包](https://github.com/xiesjdyka/EnvGuard/releases/latest/download/EnvGuard-windows-x64.zip)，打开解压后的文件夹。点击文件夹顶部地址栏，复制这个文件夹的路径。
2. **右键左下角 Windows 开始图标**，选 **终端（管理员）**，或菜单里的 **Windows PowerShell（管理员）**。Windows 询问权限时点“是”；仍用同一个 Windows 账号，不要换另一个管理员账号运行。如果终端打开的是命令提示符，切换到 PowerShell 标签页。
3. 把下面第一行引号里的“解压文件夹路径”换成你刚才复制的路径，然后把这两行粘贴到管理员窗口，按回车。

**第一行是进入文件夹，第二行才是执行代码。** 这样就不会因为管理员窗口默认在别的目录而找不到脚本。

```powershell
Set-Location -LiteralPath '解压文件夹路径'
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode Apply -Preset Basic -ConfirmAllChromeProfiles
```

例如你复制的路径是 `D:\EnvGuard`，第一行就写 `Set-Location -LiteralPath 'D:\EnvGuard'`。这里是例子，换成你自己的路径。

它会执行包内代码，设置当时那两项：限制 WebRTC 不经过代理的 UDP 连接、关闭提前加载。**影响当前 Windows 用户的所有 Chrome 窗口**，语音、视频功能可能受影响；代理软件、语言和时区不会被这行代码修改。

看到“2 项设置已写入”后，继续下一步。即使打开的是蓝色 Windows PowerShell 窗口，上面第二行也会用 `pwsh`（PowerShell 7）来运行脚本。找不到 `pwsh`，需要先 [安装 PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows)。其他报错先停下看原因，不要关闭安全软件强行运行。

## 第二步：在 Chrome 看有没有生效

1. 保存浏览器里的工作，退出所有 Chrome 窗口，再重新打开。
2. 地址栏输入 `chrome://policy`，按回车。
3. 点 **Reload policies / 重新加载政策**。
4. 找下面两项，值应当一致，状态应当是 **OK / 正常**。

| 政策名 | 应当显示的值 |
| --- | --- |
| `WebRtcIPHandling` | `disable_non_proxied_udp` |
| `NetworkPredictionOptions` | `2` |

两项都正常，这一步就好了。Chrome 出现“由组织管理”，可以在这个页面看具体是哪几项政策。

如果没出现，或状态报错，先别继续登录，保留截图排查。这里是在确认两项设置生效，不是账号安全评分。

## 第三步：移除中文，只留下英文

按当时这个专用 Chrome 的设置方式做：

1. 地址栏输入 `chrome://settings/languages`，按回车。
2. 在 **Preferred languages / 首选语言** 里确认有 **English** 或 **English (United States)**；没有就点 **Add languages / 添加语言** 加入英文。
3. 点英文右侧的三个点，选 **Move to the top / 移到顶部**。
4. 勾 **Display Google Chrome in this language / 以这种语言显示 Google Chrome**，点 **Relaunch / 重新启动**。先切换菜单语言，再移除原来的中文。
5. **把所有 Chinese / 中文条目逐个移除**：点每个中文条目右侧的三个点 → **Remove / 移除**。简体、繁体等中文条目都不要留在首选语言列表里。
6. 重新打开这个 Chrome，回到语言页面检查：**列表里只剩英文，没有任何 Chinese / 中文条目**，再刷新你看的检测页面。

**只把英文移到第一位不够，中文条目也要移除。** 当时“改了还是显示中文”，就是首选语言列表里还留着中文。这个步骤去掉的是列表中的中文信号，不会隐藏其他检查项。按钮名称可以对照 [Google 的语言设置说明](https://support.google.com/chrome/answer/173424?co=GENIE.Platform%3DDesktop&hl=zh-Hans)。

到这里就是完整流程了。字体、Emoji 那两项不用继续折腾：改默认字体不会隐藏系统字体，Microsoft style 也不是 IP 泄露提示。第三方分数不是 Claude 的账号安全判定。

<details>
<summary>以后想恢复代码改过的两项，点这里</summary>

按第一步的方法打开管理员 PowerShell 窗口，先进入同一个解压文件夹，再执行：

```powershell
pwsh -NoProfile -File .\ChromePrivacy.ps1 -Mode Restore -Preset Basic -ConfirmAllChromeProfiles
```

然后重开 Chrome，到 `chrome://policy` 核对。恢复的是**本次执行代码前**的两项值，不会撤销你用其他代码改过的旧设置，也不会恢复语言。

脚本的应用和恢复操作会留记录：已配置 EnvGuard 时，写到你选的日志；否则在 `%LOCALAPPDATA%\EnvGuard\logs\events.jsonl`。不要把含私人信息的日志公开上传。

</details>

想了解原理或其他可选设置，另看 [详细说明](CHROME-DETAILS.md)。这里只保留上面三步。

[回到 EnvGuard 使用说明](README.md)
