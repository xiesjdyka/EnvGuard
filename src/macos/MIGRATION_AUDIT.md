# EnvGuard v1.2.8 → macOS 移植审计

上游仓库：https://github.com/xiesjdyka/EnvGuard

固定基线：标签 `v1.2.8`，提交 `4900d68`（完整提交见 UPSTREAM_COMMIT.txt）。最初 clone 时为 1.2.5；收到版本更新后 fetch 并切换到 1.2.8，最终实现按 1.2.8 的规则构建。

原项目为 C# / .NET Framework 4.8 / Windows Forms，不是 Python Tkinter/PyQt。没有 winreg、win32api Python 依赖；其 Windows 专属能力是 C# 注册表、P/Invoke、WMI、SCM、AppModel、HCS 与 WinForms。

| 上游模块 | Windows 专属项 | macOS 实现 / 边界 |
|---|---|---|
| Program.cs | WindowsIdentity / WindowsPrincipal / runas / Local Mutex | getuid、主机身份、AppKit 生命周期；不提升管理员权限、不启动目标客户端 |
| Forms.cs | WinForms、Windows 应用目录与包注册表、托盘 | AppKit 原生面板、NSOpenPanel 选择 .app、NSStatusBar 菜单；关闭窗口保留状态栏，菜单退出停止监测 |
| EnvironmentChecker.cs | Microsoft.Win32.Registry、Windows SID、时区注册表、WinINet | /etc/localtime + date；defaults 语言/区域；scutil --proxy / --dns；Chrome plist 与 Preferences 保存值 |
| NativeProcesses.cs | kernel32 Toolhelp/OpenProcess/GetProcessTimes/TerminateProcess，iphlpapi TCP表，WMI 事件 | lsof 监听 PID、ps UID/路径/启动时间、CryptoKit SHA-256；紧急停止前重查身份；不是事件级全后代追踪 |
| Profile.cs | .exe/WindowsApps 路径、SID、Windows 日志位置 | .app Bundle ID / 主执行文件摘要、当前 UID/设备、私有 Application Support JSON / JSONL、原子写入 |
| MonitorEngine.cs | WMI Win32_Service、ServiceController、服务注册表 | 原生串行检测/预警/紧急关闭；只读关联 launchd plist，未赋予停止 launchd 服务权限 |
| NetworkTiming.cs | 平台无关调度规则 | 8 秒请求超时，11 秒单调时钟起始间隔，不重叠请求，Amazon/ipify 交替单站 |
| AutomaticProtection.cs | 1.2.8 新增自动保护 | 默认 OFF；确认开启并持久化后，仅出口不符或连续两轮超时触发；同一异常执行一次，恢复重置 |
| PackageUpdates.cs | AppModel 包 family/publisher/origin、WindowsApps | 不模拟 MSIX；macOS 使用 .app 的 Bundle ID/主程序 SHA-256，更新需人工重新确认，未实现同签名更新自动接续 |
| CoworkNative.cs / CoworkAdapter.cs | Windows Host Compute Service / VM 规则 | 不存在可直接等价的 macOS HCS；明确不覆盖 VM、容器或包外共享宿主，不作模糊名称强杀 |
| Build.ps1 / app.manifest | .NET csc、Windows UAC / x64 manifest | Swift/AppKit + 系统 frameworks；Universal 2 Mach-O，macOS 14+，ad-hoc 签名 |
| ChromePrivacy.ps1 | Windows 注册表策略写入/恢复 | 原件仅随上游源码保留；macOS 程序只读策略，不执行 PowerShell 或改系统/浏览器策略 |

## 1.2.8 行为对齐

- 首次配置/手动核对依次查询 Amazon 和 ipify，两站一致才允许保存核心基准。
- 监测每轮只请求一个出口站点，顺序为 Amazon → ipify → Amazon；8 秒超时、11 秒起始间隔，忙碌时不排队、不重叠。
- 任一有效出口结果与基准不符立即预警；单次未确认先复核，连续两轮未确认才发新增系统提醒。
- 自动保护仅接收“已确认出口不符”和“超时数量”，本地时区、日志、身份、DNS/WebRTC 未验证及普通非超时 HTTP 错误不触发自动结束。
- 自动保护默认关闭；旧配置缺少可选 flag 时仍关闭。模式保存失败不会更改生效状态；同一持续事件防重复，网络恢复后重新准备。
- macOS 版手动/自动关闭共用同一安全范围，拒绝并发应急操作；不会启动、下载或更新 Claude。

## 网络与隐私增强

所有 HTTP 探测经明确的 `http://127.0.0.1:20808` 代理：curl `-q` 忽略 curlrc、`--noproxy ""` 禁止绕过，不使用 -L，不关闭 TLS 证书校验，不回退直连。7890 与 20808 都核对监听所有者路径和摘要。不会自动开启中继。

## 仓库归档调整

- 本目录由用户提供的 `EnvGuard_macOS_Release.zip` 中 `NativeSource/` 归档，未覆盖包内旧的 Windows 源码到仓库；Windows 文件以作者当前仓库为准。
- 去掉测试设备预填的出口 IP、运营商和固定时区。公开源码初始 IP/运营商为空，时区取当前设备；首次人工核对后仍固化实际值。已保存配置不受改动影响。
- 无头判定测试改用公共测试地址与模拟运营商，不查询真实网络；测试覆盖公开默认值不含个人目标。
- 新增独立跨架构 CI、隔离测试和白名单打包。原包二进制不原样作为新源码构建结果发布；公开程序从归档源码重新编译，并在 BUILD_INFO.txt 记录提交。
- 原包的测试记录是供应材料，不是 Windows 维护环境重新执行的 Mac 结果。自动化结果由对应 Actions 提供。

上游真实实现没有 ISP/城市查询、DNS 泄漏实测或 WebRTC ICE 采集：它比对的是出口和保存策略。macOS 版增加通过同一中继调用 ipinfo 的 ASN/运营商/归属地（仅手动/首次核对请求），及系统 DNS 配置摘要。ISP 数据不证明“住宅 IP”。监测轮不额外查询 ipinfo，保持单出口请求规则。

实际 DNS/WebRTC 由管理员在实际沙盒浏览器独立测试后输入本次观察值；5 分钟失效。WebRTC 公网候选需等于代理出口；DNS 解析器需在用户填写白名单内。辅助项缺值或未测试显示 INFO，不否决五项核心准入；核心网络超时仍阻断，不伪造外部核验结果，不宣称已实测浏览器流量。

## 功能差异（不宣称 Windows 全功能无损复刻）

保留核心环境/出口基准监测、1.2.8 网络策略与可选自动保护。Windows 的服务控制、包自动接续、WMI 全后代与 HCS 客体不能按名称硬移植；当前是上述明确范围的原生实现，而不是 Windows 服务/VM 管理器的等价替代。关闭范围仅选定 .app 内同用户、路径和启动时间身份再次匹配的进程。包外代理、launchd 共享服务、已脱离并不在包内的后代未涵盖。

这是一款环境预警器，不是系统防火墙。被检测客户端不受自动启动拦截；红色/橙色“建议暂停”是预警，非声称已切断系统网络。

## 1.2.8-macos.2 综合判定修订

Report 现在使用独立的 core 字典核对 Clash、中继、时区、IP、运营商五项。缺失即失败，禁止用空报告或辅助 OK 推导放行；不再扫描任意 UNVERIFIED 行来决定顶栏。所有前端和 CLI 共用 overallText / overallColor。

浏览器辅助核验默认关闭，新增独立复选框；未勾选不声称“已在外部浏览器完成核验”。成功保存会固化三个当前网络/时区属性，清除首次使用行并立即刷新。保存函数支持隔离目录，测试正向持久化与写入失败回滚。

新增依赖注入 AuditDependencies 和无头 --decision-self-test：模拟已监听及双站/运营商响应，验证全通过绿色、五项逐一失败红色、未实测辅助项不否决、双站不一致拒绝保存、成功后首次标记移除、同 IP 基准 ASN 复用、写入失败回滚。没有执行真实客户端启动、修改实际个人基准或触发自动杀进程。
