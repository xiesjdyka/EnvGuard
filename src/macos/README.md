# EnvGuard macOS 1.2.8-macos.2（核心判定修订）

原生 Swift / AppKit 版本，基于 xiesjdyka/EnvGuard 的 MIT 源码与 v1.2.8 规则。**macOS 14 或更高，Apple Silicon / Intel 通用。无需 Python、.NET、Homebrew 或第三方库。**

## 双击运行

解压整个压缩包，双击 `EnvGuard.app` 或同目录的「环境预警检测器.command」。不要只拷贝源码中的某个文件。本说明不假定你的桌面已有快捷方式。

本地构建只有 ad-hoc 签名，未经 Apple Developer ID 公证。复制到其他 Mac 后，Gatekeeper 可能要求管理员按系统提示通过“右键 → 打开”或“隐私与安全性”确认来源；不需要、也不应全局关闭安全检查。macOS 14 以下不受支持。

窗口立即显示，检测在后台运行。工具不会启动 Claude 或任何选定应用，不自动开启中继、不修改时区/代理/Chrome 设置。关闭主窗口会隐藏到菜单栏；从菜单栏或 Cmd+Q 退出才停止监测。

## 首次核对

1. 自行准备前置代理 7890 与已配置的 HTTP/mixed 中继 20808。没有中继时，工具显示阻断建议并跳过外网探测。前置代理不限定 Clash 品牌；此移植版端口仍固定为 7890/20808，不会自动修改代理软件。
2. 五项核心准入为 7890 LISTEN、20808 LISTEN、预期时区、预期公网出口 IP、运营商。公开发行不预设个人 IP 或运营商：初始可留空，先看结果并人工核对，再保存。时区初始取当前设备设置，可手动填目标 IANA 时区。出口服务器地址不一定等于公网出口，保存后按本设备基准比对。
3. 需要紧急关闭功能时，点击“添加保护应用”选 .app。这里只读取 Bundle ID 和主程序摘要，不会启动应用。
4. 先点立即检测，核对出口与范围。再点确认并保存核心基准，程序重新依次查询两站，一致且核心检测通过才保存。保存成功自动固化当前 IP、运营商 / ASN 与时区，移除首次使用标记并立即刷新。未验证的 DNS/WebRTC 仅显示 INFO，不阻塞核心状态。
5. 点击开始监测：11 秒起始间隔，8 秒请求超时，Amazon/ipify 交替，监测请求不重叠。普通未确认连续两轮提示；已确认出口不符立即提示。
6. **自动紧急保护默认关闭。** 选择应用并保存本设备基准后，可明确确认开启。只有出口不符或连续两轮超时才触发；其他预警不自动杀进程。同一异常执行一次，恢复后重新准备。应用包外守护、服务与 VM 不在关闭范围。

每台新 Mac 重新确认基准。应用版本改变会提示重新选择，不会自动认定新版可信。添加/清空应用范围后需重新保存确认；模式设置独立保存。

## DNS / WebRTC

Chrome 策略及 Preferences 只是保存配置。浏览器隐私辅助核验默认不勾选。未启用时显示“未启用内置辅助核验；外部浏览器核验状态未由本工具验证”，不声称已经完成外部测试；不参与核心准入。勾选但缺少实际观察值时同样仅显示 INFO。可在实际沙盒浏览器完成独立测试后，用“浏览器隐私核验”输入观察到的 WebRTC 公网候选、DNS 解析器 IP 和预期 DNS 白名单，本次记录 5 分钟后失效。不等同于内置抓包、STUN 或 DNS 实测引擎。

## CLI

```bash
./EnvGuard.app/Contents/MacOS/EnvGuard --offline --json
./EnvGuard.app/Contents/MacOS/EnvGuard --check
./EnvGuard.app/Contents/MacOS/EnvGuard --check --expected-ip 你的公网出口 --expected-org '你的预期运营商'
./EnvGuard.app/Contents/MacOS/EnvGuard --self-test
./EnvGuard.app/Contents/MacOS/EnvGuard --decision-self-test
```

退出码：0 为五项核心准入全部通过；2 为任一核心条件异常或缺失。辅助 INFO/UNVERIFIED 不改变核心退出码。`--offline` 明确跳过网络，因此按未完成全量核验返回 2。CLI 不执行自动杀进程；自动保护只在 GUI 主动开启监测时有效。

## 数据与网络

本机配置、基准和 JSONL 日志：`~/Library/Application Support/EnvGuard-macOS/`，文件 600。导出报告可能包含公网 IP、软件路径和系统设置摘要，请按需脱敏。发行包不包含个人配置、账号密码、真实审计日志或 relay.py 凭证。

网络检查会通过 20808 访问 checkip.amazonaws.com、api.ipify.org；手动核对时还查询 ipinfo.io 的 ASN/运营商/归属地。没有直连回退，不关闭证书校验、不跟随重定向。网络数据服务异常或限流会报告；地理/运营商数据库不证明住宅属性。

## 从源码构建

在 Mac 开发机安装 Apple Command Line Tools 后，从仓库根目录运行：

```bash
bash src/macos/build.sh "$PWD/out/macos"
bash src/macos/test.sh "$PWD/out/macos"
bash src/macos/package.sh "$PWD/out/macos"
```

ZIP 在 `out/EnvGuard_macOS_Release.zip`。脚本编译 arm64/x86_64 后合并 Universal 2 并 ad-hoc 签名；只打包列出的程序、源码与说明，不打包 Application Support 配置或真实日志。发行包源码在 `NativeSource/`，也可以从那里运行 `bash build.sh`。

TestNetwork.py 与 TestFixture.c 为开发期隔离测试，前者仅在测试时需要 Python 3，后者需要 clang；运行程序不依赖它们。系统依赖详见 DEPENDENCIES.txt。

上传原包附有 Apple Silicon 测试记录；这里无法连接那台 Mac，原记录仅作移植依据。新增 GitHub Actions 在 Apple Silicon / Intel 两种 runner 分别编译、验证签名、执行无头判定与隔离代理/进程测试；结果以对应提交的 Actions 为准。模拟测试不是所有硬件上的 GUI 或真实代理连通性验证。

详细移植差异见 MIGRATION_AUDIT.md。该工具不提供全流量隔离或账号安全保证。

## 综合状态判定

五项核心全部通过显示绿色“● 环境安全 · 核心基准验证通过”。任一核心失败或没有测到，显示红色并列出具体条件。WebRTC 保存策略、DNS/WebRTC 缺少实测、浏览器或其他辅助快照信息独立显示，不再一票否决核心状态。这里的“安全”限于五项核心基准，不等于工具已确认所有浏览器流量、DNS 或 WebRTC 安全，也不自动修改网络放行规则。

首次保存要求两个站点依次成功且一致、运营商实际查询成功与五项核心通过。IP/运营商/时区写入 config.json 与 baseline.json，写入失败回滚候选基准，不显示保存成功。监测仍保持 1.2.8 的 8 秒超时与 11 秒交替单站；只有当前 IP 等于受信 IP 时才复用该 IP 的已核验 ASN，行内明确标注复用。

如果更新前旧检测器仍在运行，请先从其菜单栏退出，再打开新版；不要启动 Claude 来测试检测器。Mac 文件传到 Windows 的操作见 [局域网下载说明](LAN-DOWNLOAD.md)。
