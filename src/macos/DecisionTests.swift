import Foundation

func decisionTests() {
    func require(_ condition: Bool, _ text: String) {
        if !condition { print("[FAIL] " + text); exit(1) }
        print("[OK] " + text)
    }
    require(Config().expectedIP.isEmpty && Config().expectedOrg.isEmpty, "公开发行不预设个人出口或运营商")
    require(Config().expectedTimezone == TimeZone.current.identifier, "首次时区来自当前设备，不固定测试设备时区")
    var c = Config()
    c.expectedTimezone = "America/New_York"
    c.expectedIP = "8.8.8.8"
    c.expectedOrg = "Example Network"
    var requested: [String] = []
    func mockAudit(ports: Set<Int> = [7890,20808], zone: String = "America/New_York", ip: String = "8.8.8.8", org: String = "AS64500 Example Network", privacy: Bool = false, baseline: Baseline? = nil, endpoint: String? = nil, inconsistent: Bool = false) -> Report {
        var config = c; config.privacyVerificationEnabled = privacy
        let deps = AuditDependencies(readSnapshot: { config, r in
            r.settings["timezone"] = zone; r.core["timezone"] = zone == config.expectedTimezone
            r.add(r.core["timezone"] == true ? "OK" : "BLOCK","系统时区",zone)
            r.add("INFO","WebRTC 保存策略","模拟：未配置，不阻塞核心。")
        }, readListeners: { port in
            ports.contains(port) ? [Listener(pid:Int32(port),path:"/mock/\(port)",hash:"test-sha256")] : []
        }, request: { url in
            requested.append(url)
            if url.hasPrefix("https://ipinfo.io/") { return "{\"ip\":\"\(ip)\",\"org\":\"\(org)\",\"city\":\"Test\",\"country\":\"US\"}" }
            return inconsistent && url.contains("ipify") ? "1.1.1.1" : ip
        })
        return audit(config,online:true,baseline:baseline,endpoint:endpoint,dependencies:deps)
    }
    requested = []
    let good = mockAudit()
    require(!good.blocked && good.coreReady, "模拟 LISTEN 7890/20808、时区、预期出口及运营商全部通过")
    require(good.overallText == "● 环境安全 · 核心基准验证通过" && good.overallColor == "green", "统一状态映射为指定绿色文案")
    require(requested == ["https://checkip.amazonaws.com","https://api.ipify.org","https://ipinfo.io/8.8.8.8/json"], "双站依次探测后查询运营商，无额外端点")
    require(good.rows.filter { $0.name == "DNS / WebRTC 实际泄漏" }.allSatisfy { $0.status == "INFO" && !$0.detail.contains("已在外部浏览器完成核验") }, "未启用隐私核验仅 INFO，不虚构核验事实")
    require(!mockAudit(privacy:true).blocked, "勾选辅助核验但未填观察值仍不否决核心")
    let failures: [(Report,String)] = [
        (mockAudit(ports:[20808]), "Clash 7890"),
        (mockAudit(ports:[7890]), "中继 20808"),
        (mockAudit(zone:"America/Los_Angeles"), "系统时区"),
        (mockAudit(ip:"1.1.1.1"), "公网出口 IP"),
        (mockAudit(org:"Other Transit"), "运营商 / ASN")
    ]
    for (r,name) in failures { require(r.blocked && r.overallColor == "red" && r.coreFailures.contains(name), "\(name) 异常仍明确阻断，未因降级辅助项而放行") }
    let inconsistent = mockAudit(inconsistent:true)
    require(inconsistent.blocked && !inconsistent.networkConfirmed, "双站不一致拒绝放行")
    let temp = FileManager.default.temporaryDirectory.appendingPathComponent("EnvGuard-decision-" + UUID().uuidString)
    defer { try? FileManager.default.removeItem(at:temp) }
    do {
        let trusted = try saveBaseline(good,config:c,directory:temp)
        let stored = try JSONDecoder().decode(Baseline.self,from:Data(contentsOf:temp.appendingPathComponent("baseline.json")))
        let cfg = try JSONDecoder().decode(Config.self,from:Data(contentsOf:temp.appendingPathComponent("config.json")))
        require(trusted.expectedIP == "8.8.8.8" && cfg.expectedOrg == "AS64500 Example Network" && stored.timezone == "America/New_York" && stored.org == cfg.expectedOrg, "IP、运营商和时区已固化到临时受信基准并成功重新读取")
        let refreshed = reportAfterSave(good)
        require(!refreshed.rows.contains { $0.name == "首次使用" } && refreshed.overallColor == "green", "保存成功移除首次使用警告，立即刷新绿色")
        requested = []
        let monitored = mockAudit(baseline:stored,endpoint:"https://checkip.amazonaws.com")
        require(!monitored.blocked && requested.count == 1, "监测单站通过后，仅对同一个受信 IP 复用其已核验 ASN")
        let denied = temp.appendingPathComponent("denied")
        try FileManager.default.createDirectory(at:denied.appendingPathComponent("config.json"),withIntermediateDirectories:true)
        do { _ = try saveBaseline(good,config:c,directory:denied); require(false,"拒绝写入失败") }
        catch { require(!FileManager.default.fileExists(atPath:denied.appendingPathComponent("baseline.json").path), "配置写入失败回滚新基准，不宣称保存成功") }
    } catch { print("[FAIL] Temporary persistence test: \(error)"); exit(1) }
    print("[最终判定文本] \(good.overallText)")
    print("[最终判定颜色] \(good.overallColor) / NSColor.systemGreen")
    print("[测试范围] 全部使用模拟监听与 HTTPS 响应；仅写入临时测试目录，不修改个人基准，不启动任何 GUI 或真实中继。")
}
