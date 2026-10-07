import AppKit
import Foundation
import UniformTypeIdentifiers
import Darwin

func argument(_ flag: String) -> String? { guard let i = CommandLine.arguments.firstIndex(of: flag), i+1 < CommandLine.arguments.count else { return nil }; return CommandLine.arguments[i+1] }
func list(_ text: String) -> [String] { text.split(whereSeparator: { $0 == "," || $0 == " " || $0 == "\n" }).map(String.init) }
func policyTests() {
    func require(_ b: Bool, _ name: String) { if !b { print("[FAIL] " + name); exit(1) }; print("[OK] " + name) }
    require(Config().autoKillOnAnomaly != true, "1.2.8 automatic protection defaults OFF")
    var p = AutomaticPolicy()
    p.observe(healthy: false, mismatch: false, timeouts: 1); require(!p.claim(enabled: true), "single timeout never kills")
    p.observe(healthy: false, mismatch: false, timeouts: 1); require(!p.claim(enabled: false) && p.claim(enabled: true), "two timeout rounds eligible only when enabled")
    require(!p.claim(enabled: true), "one claim per incident")
    p.observe(healthy: true, mismatch: false, timeouts: 0)
    p.observe(healthy: false, mismatch: true, timeouts: 0); require(p.claim(enabled: true), "healthy rearms; mismatch immediately eligible")
    p.observe(healthy: true, mismatch: false, timeouts: 0)
    p.observe(healthy: false, mismatch: false, timeouts: 1)
    p.observe(healthy: false, mismatch: false, timeouts: 0)
    p.observe(healthy: false, mismatch: false, timeouts: 1); require(!p.claim(enabled: true), "non-timeout breaks timeout streak")
    var schedule = NetworkSchedule(); require(schedule.due(now: 0, inFlight: false), "initial schedule due")
    schedule.started(now: 2); require(!schedule.due(now: 12, inFlight: false) && schedule.due(now: 13, inFlight: false) && !schedule.due(now: 13, inFlight: true), "11s monotonic start-to-start; never overlaps")
    print("[OK] Policy tests are pure; no real protected processes or network involved.")
}
if CommandLine.arguments.contains("--self-test") { _ = selfTest(); policyTests(); exit(0) }
if CommandLine.arguments.contains("--decision-self-test") { decisionTests(); exit(0) }
if let portText = argument("--proxy-contract-test"), let port = Int(portText), (1024...65535).contains(port) {
    do { _ = try proxied("https://envguard-test.invalid",proxyPort:port); print("[FAIL] Proxy rejection ignored"); exit(1) }
    catch is ProbeError { print("[OK] Explicit proxy failure surfaced; no fallback performed."); exit(0) }
    catch { print("[FAIL] Unexpected proxy test error"); exit(1) }
}
if let fixturePath = argument("--scope-fixture-test") {
    let url = URL(fileURLWithPath:fixturePath)
    do {
        let target = try selectApp(url)
        guard target.bundleID == "org.envguard.testfixture", url.lastPathComponent == "ScopedFixture.app", URL(fileURLWithPath:target.executable).lastPathComponent == "EnvGuardFixture" else { throw GuardError(message:"Not an EnvGuard test fixture") }
        let fixture = Process(); fixture.executableURL = URL(fileURLWithPath:target.executable); try fixture.run()
        let unrelated = Process(); unrelated.executableURL = URL(fileURLWithPath:"/bin/sleep"); unrelated.arguments = ["20"]; try unrelated.run()
        defer { if fixture.isRunning { fixture.terminate() }; if unrelated.isRunning { unrelated.terminate() }; fixture.waitUntilExit(); unrelated.waitUntilExit() }
        Thread.sleep(forTimeInterval:0.2)
        var c = Config(); c.apps = [target]
        guard processScope(c.apps).contains(where: { $0.pid == fixture.processIdentifier }) else { throw GuardError(message:"Fixture attribution failed") }
        let result = emergencyStop(c)
        fixture.waitUntilExit()
        guard unrelated.isRunning, processScope(c.apps).isEmpty else { throw GuardError(message:"Scope invariant failed") }
        print("[OK] Scoped test fixture ended; unrelated process preserved; no GUI or real client involved.")
        print(result)
        exit(0)
    } catch { print("[FAIL] Fixture test: \(error)"); exit(1) }
}
if CommandLine.arguments.contains("--check") || CommandLine.arguments.contains("--offline") {
    var c = Config.load()
    if let timezone = argument("--timezone") { c.expectedTimezone = timezone }
    if let ip = argument("--expected-ip") { c.expectedIP = ip }
    if let org = argument("--expected-org") { c.expectedOrg = org }
    if let dns = argument("--allowed-dns") { c.expectedDNS = list(dns) }
    var observation = Observation()
    if let rtc = argument("--webrtc-ip") { observation.webrtc = list(rtc); observation.at = Date() }
    if let dns = argument("--dns-observed") { observation.dns = list(dns); observation.at = Date() }
    let r = audit(c, online: !CommandLine.arguments.contains("--offline"), observation: observation, baseline: loadBaseline())
    if CommandLine.arguments.contains("--json"), let data = try? JSONEncoder().encode(r), let text = String(data: data, encoding: .utf8) { print(text) }
    else { print("EnvGuard macOS \(version)\n\n" + r.text + "\n\n" + (r.overallText + " · color=" + r.overallColor)) }
    exit(r.blocked ? 2 : 0)
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    var lockFD: Int32 = -1
    var window: NSWindow!
    let status = NSTextField(labelWithString: "正在准备只读自检…")
    let body = NSTextView()
    let expectedIP = NSTextField(); let expectedOrg = NSTextField(); let timezone = NSTextField()
    let privacyToggle = NSButton(checkboxWithTitle:"启用浏览器隐私辅助核验（不参与核心准入）",target:nil,action:nil)
    let privacyButton = NSButton(title:"浏览器隐私核验",target:nil,action:nil)
    let auto = NSButton(checkboxWithTitle: "自动紧急保护（默认关闭）", target: nil, action: nil)
    let checkButton = NSButton(title: "立即检测", target: nil, action: nil)
    let baselineButton = NSButton(title: "确认并保存核心基准", target: nil, action: nil)
    let monitorButton = NSButton(title: "开始监测", target: nil, action: nil)
    var config = Config.load(); var latest: Report?; var lastConfig: Config?
    var observation = Observation(); var busy = false; var monitoring = false; var emergencyBusy = false
    var schedule = NetworkSchedule(); var round = 0; var networkFailures = 0
    var policy = AutomaticPolicy(); var signature = ""
    var timer: Timer?; var tray: NSStatusItem!
    var checkStarted: TimeInterval = 0
    var displayedConfigKey = ""
    func label(_ text: String, size: CGFloat = 13, weight: NSFont.Weight = .regular) -> NSTextField {
        let v = NSTextField(wrappingLabelWithString: text); v.font = .systemFont(ofSize: size, weight: weight); return v
    }
    func button(_ title: String, _ action: Selector) -> NSButton { let b = NSButton(title: title, target: self, action: action); b.bezelStyle = .rounded; return b }
    func row(_ views: [NSView]) -> NSStackView { let s = NSStackView(views: views); s.orientation = .horizontal; s.spacing = 10; s.alignment = .centerY; return s }
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        if argument("--ui-smoke") == nil {
            do { try FileManager.default.createDirectory(at:dataRoot,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700]) } catch { self.error("无法创建本机配置目录。"); NSApp.terminate(nil); return }
            lockFD = Darwin.open(dataRoot.appendingPathComponent("instance.lock").path, O_RDWR | O_CREAT | O_NOFOLLOW, mode_t(0o600))
            if lockFD < 0 || flock(lockFD, LOCK_EX | LOCK_NB) != 0 { self.error("EnvGuard 已运行或无法建立独占锁，请查看菜单栏 EG。"); NSApp.terminate(nil); return }
        }
        window = NSWindow(contentRect: NSRect(x:0,y:0,width:1000,height:800), styleMask:[.titled,.closable,.miniaturizable,.resizable], backing:.buffered, defer:false)
        window.appearance = NSAppearance(named:.aqua)
        window.contentView!.wantsLayer = true
        window.contentView!.layer!.backgroundColor = NSColor(calibratedWhite:0.97,alpha:1).cgColor
        window.title = "EnvGuard macOS · 1.2.8"; window.center(); window.delegate = self; window.minSize = NSSize(width:900,height:720)
        let outer = NSStackView(); outer.orientation = .vertical; outer.spacing = 14; outer.alignment = .leading
        outer.edgeInsets = NSEdgeInsets(top:24,left:26,bottom:22,right:26); outer.translatesAutoresizingMaskIntoConstraints = false
        window.contentView!.addSubview(outer)
        NSLayoutConstraint.activate([outer.leadingAnchor.constraint(equalTo:window.contentView!.leadingAnchor),outer.trailingAnchor.constraint(equalTo:window.contentView!.trailingAnchor),outer.topAnchor.constraint(equalTo:window.contentView!.topAnchor),outer.bottomAnchor.constraint(equalTo:window.contentView!.bottomAnchor)])
        outer.addArrangedSubview(label("EnvGuard",size:30,weight:.bold))
        outer.addArrangedSubview(label("环境预警 · 核对出口与系统基准 · 原生 macOS 1.2.8",size:14,weight:.medium))
        let hint = label("只检查与预警，不启动 Claude、不修改代理。自动保护需手动开启，且只作用于你选择并确认的应用包内进程。",size:12)
        hint.textColor = .secondaryLabelColor; outer.addArrangedSubview(hint)
        status.font = .systemFont(ofSize:16,weight:.semibold); status.textColor = .systemOrange; outer.addArrangedSubview(status)
        expectedIP.placeholderString = "可选；留空时首次核对后保存"; expectedIP.stringValue = config.expectedIP
        expectedOrg.placeholderString = "可选，例如 Verizon Business"; expectedOrg.stringValue = config.expectedOrg
        timezone.stringValue = config.expectedTimezone
        for field in [expectedIP,expectedOrg,timezone] { field.font = .systemFont(ofSize:13); field.widthAnchor.constraint(greaterThanOrEqualToConstant:250).isActive = true }
        let grid = NSGridView(views:[[label("预期出口 IP"),expectedIP],[label("运营商包含"),expectedOrg],[label("预期时区"),timezone]])
        grid.rowSpacing = 8; grid.columnSpacing = 16; outer.addArrangedSubview(grid)
        checkButton.target = self; checkButton.action = #selector(check)
        baselineButton.target = self; baselineButton.action = #selector(save)
        monitorButton.target = self; monitorButton.action = #selector(toggleMonitor)
        privacyButton.target = self; privacyButton.action = #selector(privacy)
        privacyToggle.target = self; privacyToggle.action = #selector(togglePrivacy)
        privacyToggle.state = config.privacyVerificationEnabled == true ? .on : .off
        privacyButton.isEnabled = privacyToggle.state == .on
        let controls = row([checkButton,baselineButton,monitorButton,privacyButton]); outer.addArrangedSubview(controls)
        let scope = row([button("添加保护应用…",#selector(addApp)),button("查看 / 清空范围",#selector(scope)),button("Chrome 资料…",#selector(chrome)),button("导出报告…",#selector(exportReport))]); outer.addArrangedSubview(scope)
        auto.target = self; auto.action = #selector(toggleAuto); auto.state = config.autoKillOnAnomaly == true ? .on : .off
        outer.addArrangedSubview(row([auto,button("紧急关闭选定应用",#selector(emergency))]))
        outer.addArrangedSubview(privacyToggle)
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.borderType = .bezelBorder
        body.isEditable = false; body.isSelectable = true; body.font = .monospacedSystemFont(ofSize:12,weight:.regular)
        body.backgroundColor = .textBackgroundColor; body.textContainerInset = NSSize(width:14,height:14)
        body.autoresizingMask = [.width]; body.isVerticallyResizable = true; body.isHorizontallyResizable = false
        body.textContainer?.widthTracksTextView = true
        scroll.documentView = body; outer.addArrangedSubview(scroll)
        scroll.widthAnchor.constraint(equalTo:outer.widthAnchor,constant:-52).isActive = true
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant:200).isActive = true
        scroll.setContentHuggingPriority(.defaultLow,for:.vertical)
        let foot = label("链路 127.0.0.1:20808 → Clash 7890 → 落地代理  |  8 秒超时 · 11 秒交替单站  |  辅助隐私信息不参与五项核心准入",size:11)
        foot.textColor = .secondaryLabelColor; outer.addArrangedSubview(foot)
        let menu = NSMenu(); let root = NSMenuItem(); menu.addItem(root)
        let appMenu = NSMenu(); appMenu.addItem(withTitle:"退出 EnvGuard",action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q"); root.submenu = appMenu; NSApp.mainMenu = menu
        tray = NSStatusBar.system.statusItem(withLength:NSStatusItem.variableLength); tray.button?.title = "EG · 待核对"
        let trayMenu = NSMenu(); trayMenu.addItem(withTitle:"显示自检面板",action:#selector(show),keyEquivalent:"").target = self
        trayMenu.addItem(withTitle:"退出 EnvGuard",action:#selector(NSApplication.terminate(_:)),keyEquivalent:""); tray.menu = trayMenu
        window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps:true)
        timer = Timer.scheduledTimer(withTimeInterval:0.25,repeats:true) { [weak self] _ in self?.tick() }
        if let path = argument("--ui-smoke") {
            var mock = Report(); mock.networkConfirmed = true
            for (key,_) in Report.criteria { mock.core[key] = true }
            status.stringValue = mock.overallText + "（模拟）"; status.textColor = .systemGreen
            body.string = "[OK] Clash 7890 / 中继 20808\n  模拟 LISTEN · 无真实端口操作\n\n[OK] 公网出口 IP\n  模拟 8.8.8.8 · 两站一致\n\n[OK] 运营商 / ASN\n  模拟 AS64500 Example Network\n\n[OK] 系统时区\n  模拟 America/New_York\n\n[INFO] WebRTC 保存策略\n  未配置，辅助信息，不阻塞核心状态。\n\n[INFO] DNS / WebRTC\n  未启用内置核验；不声称外部核验已完成。"
            DispatchQueue.main.asyncAfter(deadline:.now()+0.5) {
                if let view = self.window.contentView, let rep = view.bitmapImageRepForCachingDisplay(in:view.bounds) {
                    view.cacheDisplay(in:view.bounds,to:rep)
                    try? rep.representation(using:.png,properties:[:])?.write(to:URL(fileURLWithPath:path))
                }
                NSApp.terminate(nil)
            }
        } else { DispatchQueue.main.async { self.check() } }
    }
    func formConfig() throws -> Config {
        var c = config
        c.expectedIP = expectedIP.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        c.expectedOrg = expectedOrg.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        c.expectedTimezone = timezone.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        if !c.expectedIP.isEmpty && publicIP(c.expectedIP) == nil { throw GuardError(message:"预期 IP 必须是有效公网 IP。") }
        guard TimeZone(identifier:c.expectedTimezone) != nil else { throw GuardError(message:"请输入有效 IANA 时区。") }
        return c
    }
    func error(_ text: String) { let a = NSAlert(); a.messageText = "未能完成"; a.informativeText = text; a.alertStyle = .warning; a.runModal() }
    func tick() {
        if monitoring && schedule.due(now:ProcessInfo.processInfo.systemUptime,inFlight:busy || emergencyBusy) { runCheck(monitor:true) }
    }
    @objc func show() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps:true) }
    @objc func check() { runCheck(monitor:false) }
    func runCheck(monitor: Bool, capture: Bool = false) {
        if busy || emergencyBusy { return }
        let c: Config
        do { c = monitor ? config : try formConfig() } catch { self.error((error as? GuardError)?.message ?? error.localizedDescription); return }
        if capture { monitoring = false; monitorButton.title = "开始监测" }
        busy = true; checkButton.isEnabled = false; baselineButton.isEnabled = false; auto.isEnabled = false; privacyToggle.isEnabled = false; privacyButton.isEnabled = false
        status.stringValue = capture ? "重新核对两站出口后保存基准…" : "正在检测 · 不启动任何客户端…"
        let b = capture ? nil : loadBaseline(); let o = observation
        var endpoint: String? = nil
        if monitor {
            endpoint = round % 2 == 0 ? "https://checkip.amazonaws.com" : "https://api.ipify.org"; round += 1
            schedule.started(now:ProcessInfo.processInfo.systemUptime)
        }
        DispatchQueue.global(qos:.userInitiated).async {
            var report = audit(c,online:true,observation:o,baseline:b,endpoint:endpoint)
            do { try auditLog(report) } catch { report.add("BLOCK","审计日志","日志写入失败，不能静默视为正常。") }
            DispatchQueue.main.async {
                self.busy = false; self.checkButton.isEnabled = true; self.baselineButton.isEnabled = true; self.auto.isEnabled = true; self.privacyToggle.isEnabled = true; self.privacyButton.isEnabled = self.config.privacyVerificationEnabled == true
                self.lastConfig = c
                self.applyReport(report)
                if monitor && report.networkChecked {
                    self.networkFailures = report.networkConfirmed ? 0 : self.networkFailures+1
                    self.policy.observe(healthy:report.networkConfirmed,mismatch:report.exitMismatch,timeouts:report.timeoutCount)
                    let causes = report.rows.filter { $0.status == "BLOCK" }.map { $0.name+":"+$0.detail }.joined(separator:"\n")
                    let immediateLocal = report.rows.contains { $0.status == "BLOCK" && $0.name != "公网出口未确认" }
                    if report.exitMismatch || immediateLocal || self.networkFailures >= 2 {
                        if causes != self.signature { self.signature = causes; NSSound.beep(); NSApp.requestUserAttention(.informationalRequest) }
                    } else if report.networkConfirmed { self.signature = "" }
                    if self.policy.claim(enabled:self.config.autoKillOnAnomaly == true) { self.performEmergency(automatic:true) }
                }
                if capture {
                    do {
                        let trusted = try saveBaseline(report,config:c); self.config = trusted
                        self.expectedIP.stringValue = trusted.expectedIP
                        self.expectedOrg.stringValue = trusted.expectedOrg
                        self.timezone.stringValue = trusted.expectedTimezone
                        self.applyReport(reportAfterSave(report))
                    } catch { self.error((error as? GuardError)?.message ?? error.localizedDescription) }
                }
            }
        }
    }
    func applyReport(_ report: Report) {
        latest = report
        status.stringValue = report.overallText
        status.textColor = report.overallColor == "green" ? .systemGreen : .systemRed
        tray.button?.title = report.blocked ? "EG · 核心异常" : "EG · 核心通过"
        body.string = "检测时间：\(report.time)\n核心结论只覆盖五项准入条件，不代表已验证全部浏览器流量。\n\n" + report.text
    }
    @objc func togglePrivacy() {
        config.privacyVerificationEnabled = privacyToggle.state == .on
        privacyButton.isEnabled = config.privacyVerificationEnabled == true
        check()
    }
    @objc func save() {
        let a = NSAlert(); a.messageText = "确认保存当前设备核心基准？"
        a.informativeText = "请先核对出口 IP 与应用范围。保存时会重新依次检测两站，任一失败或不一致就拒绝保存。保存当前 IP、运营商与时区；未实测的辅助隐私项仅作 INFO，不阻塞核心准入。"
        a.addButton(withTitle:"重新核对并保存"); a.addButton(withTitle:"取消")
        if a.runModal() == .alertFirstButtonReturn { runCheck(monitor:false,capture:true) }
    }
    @objc func toggleMonitor() {
        if monitoring { monitoring = false; monitorButton.title = "开始监测"; return }
        guard let b = loadBaseline(), b.uid == getuid(), b.device == (Host.current().localizedName ?? "") else { error("请先确认并保存本设备、当前用户的核心基准。"); return }
        monitoring = true; round = 0; networkFailures = 0; policy = AutomaticPolicy(); schedule = NetworkSchedule(); monitorButton.title = "停止监测"; tick()
    }
    @objc func toggleAuto() {
        let enabled = auto.state == .on
        if enabled {
            guard !config.apps.isEmpty, let b = loadBaseline(), b.uid == getuid(), b.device == (Host.current().localizedName ?? "") else { auto.state = .off; error("请先添加保护应用并保存本设备、当前用户的基准。"); return }
            let a = NSAlert(); a.messageText = "启用自动紧急保护？"
            a.informativeText = "已确认出口不符，或连续两轮网络超时，将强制结束已选择、身份匹配的应用包内进程，不等待保存工作。其他告警不会触发自动关闭。不会启动 Claude。"
            a.addButton(withTitle:"确认启用"); a.addButton(withTitle:"取消")
            if a.runModal() != .alertFirstButtonReturn { auto.state = .off; return }
        }
        do {
            var updated = config; updated.autoKillOnAnomaly = enabled
            var event = Report(); event.add("WARNING","automatic_mode_change_prepared", "enabled=\(enabled)")
            try auditLog(event)
            try atomicJSON(updated,to:configURL); config = updated
            event.rows = []; event.add("WARNING","automatic_mode_changed", "enabled=\(enabled)")
            do { try auditLog(event) } catch { self.error("模式已保存，但结果日志写入失败。") }
            body.string += "\n\n[MODE] 自动紧急保护：\(enabled ? "开启" : "关闭")"
        } catch { auto.state = config.autoKillOnAnomaly == true ? .on : .off; self.error("模式保存失败，未更改生效状态。") }
    }
    @objc func addApp() {
        if monitoring || busy || emergencyBusy { error("请先停止监测并等待当前检测完成，再修改关闭范围。"); return }
        let p = NSOpenPanel(); p.canChooseDirectories = false; p.canChooseFiles = true; p.allowedContentTypes = [.applicationBundle]; p.directoryURL = URL(fileURLWithPath:"/Applications")
        guard p.runModal() == .OK, let url = p.url else { return }
        do { let target = try selectApp(url); config.apps.removeAll { $0.path == target.path }; config.apps.append(target); error("已添加 \(url.lastPathComponent)。范围仅限此 .app 包内的同用户进程；请重新确认基准。"); }
        catch { self.error((error as? GuardError)?.message ?? error.localizedDescription) }
    }
    @objc func scope() {
        if monitoring || busy || emergencyBusy { error("请先停止监测并等待检测完成，再修改范围。"); return }
        let apps = config.apps.map { "\($0.path)\n  \($0.bundleID)" }.joined(separator:"\n")
        let processes = processScope(config.apps).map { "PID \($0.pid) · \($0.path)" }.joined(separator:"\n")
        let a = NSAlert(); a.messageText = "已选择的应用与当前关闭范围"
        a.informativeText = (apps.isEmpty ? "尚未选择应用。" : apps)+"\n\n"+processes+"\n\n不覆盖包外 launchd 服务或 VM/容器。"
        a.addButton(withTitle:"保留"); a.addButton(withTitle:"清空所选应用")
        if a.runModal() == .alertSecondButtonReturn { config.apps = []; config.autoKillOnAnomaly = false; auto.state = .off }
    }
    @objc func chrome() {
        let p = NSOpenPanel(); p.title = "选择实际 Chrome 资料中的 Preferences（可跳过）"
        p.directoryURL = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/Google/Chrome")
        if p.runModal() == .OK, let url = p.url { config.chromePreferences = url.path; check() }
    }
    @objc func privacy() {
        let a = NSAlert(); a.messageText = "本次浏览器观察值核对"
        a.informativeText = "从正在使用的沙盒浏览器手动测试后填写。逗号分隔；记录 5 分钟后失效。不自动启动浏览器，也不凭系统 DNS 地址宣称无泄漏。"
        let rtc = NSTextField(); rtc.placeholderString = "观察到的 WebRTC 公网候选 IP"
        let dns = NSTextField(); dns.placeholderString = "观察到的 DNS 解析器公网 IP"
        let allowed = NSTextField(); allowed.placeholderString = "预期 DNS 解析器白名单"; allowed.stringValue = config.expectedDNS.joined(separator:",")
        let stack = NSStackView(views:[rtc,dns,allowed]); stack.orientation = .vertical; stack.spacing = 8; stack.frame = NSRect(x:0,y:0,width:480,height:100)
        a.accessoryView = stack; a.addButton(withTitle:"核对本次观察"); a.addButton(withTitle:"取消")
        if a.runModal() == .alertFirstButtonReturn { config.privacyVerificationEnabled = true; privacyToggle.state = .on; observation = Observation(webrtc:list(rtc.stringValue),dns:list(dns.stringValue),at:Date()); config.expectedDNS = list(allowed.stringValue); check() }
    }
    @objc func exportReport() {
        guard let r = latest else { return }
        let p = NSSavePanel(); p.nameFieldStringValue = "EnvGuard-report.json"
        if p.runModal() == .OK, let url = p.url { do { try atomicJSON(r,to:url) } catch { self.error("导出失败。") } }
    }
    @objc func emergency() {
        guard !config.apps.isEmpty else { error("请先选择需要保护的 .app。"); return }
        let a = NSAlert(); a.messageText = "强制结束选定应用？"; a.informativeText = "可能丢失未保存工作。仅结束身份验证通过的应用包内同用户进程。"
        a.addButton(withTitle:"立即强制结束"); a.addButton(withTitle:"取消")
        if a.runModal() == .alertFirstButtonReturn { performEmergency(automatic:false) }
    }
    func performEmergency(automatic: Bool) {
        if emergencyBusy { return }; emergencyBusy = true
        let c = config
        DispatchQueue.global().async {
            let result = emergencyStop(c)
            var r = Report(); r.add("WARNING",automatic ? "自动紧急保护结果" : "手动紧急关闭结果",result); try? auditLog(r)
            DispatchQueue.main.async { self.emergencyBusy = false; self.body.string += "\n\n"+r.text; self.error(result) }
        }
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { window.orderOut(nil); return false }
    func applicationWillTerminate(_ notification: Notification) { if lockFD >= 0 { close(lockFD) }; timer?.invalidate() }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}
let app = NSApplication.shared
let delegate = AppDelegate(); app.delegate = delegate
app.run()
