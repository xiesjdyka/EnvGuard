import Foundation
import CryptoKit
import Darwin

let version = "1.2.8-macos.2"
let dataRoot = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/EnvGuard-macOS")
let baselineURL = dataRoot.appendingPathComponent("baseline.json")
let configURL = dataRoot.appendingPathComponent("config.json")

struct CommandResult { var code: Int32; var output: String }
func command(_ tool: String, _ args: [String], timeout: Double = 4) -> CommandResult {
    let p = Process(); p.executableURL = URL(fileURLWithPath: tool); p.arguments = args
    let pipe = Pipe(); p.standardOutput = pipe; p.standardError = pipe
    // Drain concurrently to prevent pipe-buffer deadlocks for scutil output.
    final class Buffer: @unchecked Sendable { var data = Data(); let lock = NSLock() }
    let buffer = Buffer(); let group = DispatchGroup(); group.enter()
    do { try p.run() } catch { return CommandResult(code: 127, output: "工具不可用：\(tool)") }
    DispatchQueue.global().async {
        let d = pipe.fileHandleForReading.readDataToEndOfFile()
        buffer.lock.lock(); buffer.data = d; buffer.lock.unlock(); group.leave()
    }
    let deadline = Date().addingTimeInterval(timeout)
    while p.isRunning && Date() < deadline { Thread.sleep(forTimeInterval: 0.025) }
    let timedOut = p.isRunning
    if timedOut { p.terminate(); Thread.sleep(forTimeInterval: 0.1); if p.isRunning { kill(p.processIdentifier, SIGKILL) } }
    p.waitUntilExit(); group.wait()
    return CommandResult(code: timedOut ? 124 : p.terminationStatus, output: String(data: buffer.data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? "")
}
func digest(_ text: String) -> String { SHA256.hash(data: Data(text.utf8)).map { String(format: "%02x", $0) }.joined() }
func fileHash(_ path: String) throws -> String {
    let f = try FileHandle(forReadingFrom: URL(fileURLWithPath: path)); defer { try? f.close() }
    var hash = SHA256()
    while let chunk = try f.read(upToCount: 1024 * 1024), !chunk.isEmpty { hash.update(data: chunk) }
    return hash.finalize().map { String(format: "%02x", $0) }.joined()
}
func atomicJSON<T: Encodable>(_ value: T, to url: URL) throws {
    try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    try encoder.encode(value).write(to: url, options: .atomic)
    try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
}
struct AppTarget: Codable { var path: String; var executable: String; var bundleID: String; var hash: String }
struct Config: Codable {
    var expectedTimezone = TimeZone.current.identifier
    var expectedIP = ""
    var expectedOrg = ""
    var expectedDNS: [String] = []
    var chromePreferences = ""
    var apps: [AppTarget] = []
    var autoKillOnAnomaly: Bool? = false
    var privacyVerificationEnabled: Bool? = false
    static func load() -> Config {
        var c = (try? JSONDecoder().decode(Config.self, from: Data(contentsOf: configURL))) ?? Config()
        let b = loadBaseline()
        if c.expectedIP.isEmpty { c.expectedIP = b?.ip ?? "" }
        if c.expectedOrg.isEmpty { c.expectedOrg = b?.org ?? b?.settings["exitOrg"] ?? "" }
        return c
    }
}
struct Baseline: Codable { var version: Int = 1; var capturedAt: String; var device: String; var uid: UInt32; var ip: String; var settings: [String:String]; var org: String? = nil; var timezone: String? = nil }
struct Observation { var webrtc: [String] = []; var dns: [String] = []; var at = Date.distantPast }
struct Row: Codable { var status: String; var name: String; var detail: String }
struct Report: Codable {
    var time = ISO8601DateFormatter().string(from: Date())
    var rows: [Row] = []; var settings: [String:String] = [:]; var addresses: [String] = []
    var networkConfirmed = false; var confirmedChange = false
    var exitMismatch = false; var timeoutCount = 0; var networkChecked = false; var endpoint = "setup"
    var core: [String:Bool] = [:]
    static let criteria = [("clash", "Clash 7890"), ("relay", "中继 20808"), ("timezone", "系统时区"), ("ip", "公网出口 IP"), ("org", "运营商 / ASN")]
    var coreFailures: [String] { Self.criteria.filter { core[$0.0] != true }.map { $0.1 } }
    var blocked: Bool { !coreFailures.isEmpty }
    var coreReady: Bool { networkConfirmed && !blocked }
    var overallText: String { blocked ? "● 环境异常 · " + coreFailures.joined(separator: "、") + " 未通过" : "● 环境安全 · 核心基准验证通过" }
    var overallColor: String { blocked ? "red" : "green" }
    mutating func add(_ status: String, _ name: String, _ detail: String) { rows.append(Row(status: status, name: name, detail: detail)) }
    var text: String { rows.map { "[\($0.status)] \($0.name)\n  \($0.detail)" }.joined(separator: "\n\n") }
}
struct GuardError: Error { let message: String }
func publicIP(_ text: String) -> String? {
    let s = text.trimmingCharacters(in: .whitespacesAndNewlines)
    var v4 = in_addr(); var v6 = in6_addr()
    if inet_pton(AF_INET, s, &v4) == 1 {
        let bytes = withUnsafeBytes(of: v4.s_addr) { Array($0) }
        let a = bytes[0], b = bytes[1], c = bytes[2]
        if a == 0 || a == 10 || a == 127 || a >= 224 || (a == 169 && b == 254) || (a == 172 && (16...31).contains(b)) || (a == 192 && b == 168) || (a == 100 && (64...127).contains(b)) || (a == 198 && (b == 18 || b == 19)) || (a == 192 && b == 0 && (c == 0 || c == 2)) || (a == 198 && b == 51 && c == 100) || (a == 203 && b == 0 && c == 113) { return nil }
        return s
    }
    if inet_pton(AF_INET6, s, &v6) == 1 {
        let bytes = withUnsafeBytes(of: v6) { Array($0) }
        if bytes[0] == 0 || bytes[0] == 0xff || bytes[0] & 0xfe == 0xfc || (bytes[0] == 0xfe && bytes[1] & 0xc0 == 0x80) || (bytes[0] == 0x20 && bytes[1] == 1 && bytes[2] == 0x0d && bytes[3] == 0xb8) { return nil }
        var out = [CChar](repeating: 0, count: Int(INET6_ADDRSTRLEN)); _ = inet_ntop(AF_INET6, &v6, &out, socklen_t(INET6_ADDRSTRLEN))
        return String(cString: out)
    }
    return nil
}
func classify(expected: String, addresses: [String]) -> [String] {
    var errors: [String] = []
    if addresses.count == 2 && addresses[0] != addresses[1] { errors.append("两个独立出口检测结果不一致。") }
    if !expected.isEmpty && addresses.contains(where: { $0 != expected }) { errors.append("出口与确认的预期 IP 不一致。") }
    return errors
}
struct Listener { var pid: Int32; var path: String; var hash: String }
func listeners(_ port: Int) throws -> [Listener] {
    let r = command("/usr/sbin/lsof", ["-nP", "-a", "-iTCP:\(port)", "-sTCP:LISTEN", "-Fpn"])
    if r.code == 1 && r.output.isEmpty { return [] }
    if r.code != 0 { throw GuardError(message: "lsof 无法确认端口所有者。") }
    var result: [Listener] = []; var pid: Int32 = 0
    for line in r.output.split(separator: "\n") {
        if line.hasPrefix("p") { pid = Int32(line.dropFirst()) ?? 0 }
        if line == "n127.0.0.1:\(port)" || line == "n*:\(port)" {
            let path = command("/bin/ps", ["-ww", "-p", "\(pid)", "-o", "comm="])
            if path.code != 0 || path.output.isEmpty { throw GuardError(message: "监听进程路径不可读。") }
            result.append(Listener(pid: pid, path: path.output, hash: try fileHash(path.output)))
        }
    }
    return result
}
struct ProbeError: Error { let code: Int32 }
func proxied(_ url: String, proxyPort: Int = 20808) throws -> String {
    // -q ignores curlrc; explicit proxy plus empty no-proxy excludes bypass.
    let r = command("/usr/bin/curl", ["-q", "--silent", "--show-error", "--fail", "--max-time", "8", "--connect-timeout", "8", "--max-filesize", "65536", "--proto", "=https", "--proxy", "http://127.0.0.1:\(proxyPort)", "--noproxy", "", url], timeout: 9)
    if r.code != 0 || r.output.utf8.count > 65536 { throw ProbeError(code: r.code) }
    return r.output
}
let policyNames = ["WebRtcIPHandling", "DnsOverHttpsMode", "QuicAllowed", "ProxyMode", "ProxyServer", "ProxyBypassList", "ProxyPacUrl", "NetworkPredictionOptions", "BackgroundModeEnabled", "SyncDisabled"]
func systemSnapshot(_ config: Config, into r: inout Report) {
    let localtime = URL(fileURLWithPath: "/etc/localtime").resolvingSymlinksInPath().path
    let zone = localtime.components(separatedBy: "/zoneinfo/").last ?? "未知"
    let offset = command("/bin/date", ["+%Z %z"])
    r.settings["timezone"] = zone
    r.core["timezone"] = zone == config.expectedTimezone && offset.code == 0
    r.add(zone == config.expectedTimezone && offset.code == 0 ? "OK" : "BLOCK", "系统时区", "\(zone) · \(offset.output)；预期 \(config.expectedTimezone)。UTC 偏移仅展示，不冻结正常夏令时。")
    for (key, args) in [("locale", ["read", "NSGlobalDomain", "AppleLocale"]), ("languages", ["read", "NSGlobalDomain", "AppleLanguages"])] {
        let result = command("/usr/bin/defaults", args)
        r.settings[key] = result.code == 0 ? result.output : "未设置"
    }
    for (key, args) in [("systemProxy", ["--proxy"]), ("dns", ["--dns"])] {
        let result = command("/usr/sbin/scutil", args)
        if result.code == 0 { r.settings[key] = digest(result.output) }
        else { r.add("BLOCK", key, "系统设置读取失败。") }
    }
    r.add("OK", "系统 API", "已读取 defaults 全局语言/区域、scutil 系统代理与 DNS 快照；记录摘要以减少敏感地址落盘。")
    let domains = ["/Library/Managed Preferences/com.google.Chrome", FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Managed Preferences/com.google.Chrome").path, "com.google.Chrome"]
    var rtcFound = false
    for (index, domain) in domains.enumerated() {
        for key in policyNames {
            let value = command("/usr/bin/defaults", ["read", domain, key], timeout: 1)
            if value.code == 0 {
                r.settings["Chrome/\(index)/\(key)"] = digest(value.output)
                if key == "WebRtcIPHandling" {
                    rtcFound = true
                    r.add(value.output == "disable_non_proxied_udp" ? "OK" : "INFO", "WebRTC 保存策略", "读取值：\(value.output)。不代表正在使用的浏览器已应用该策略。")
                }
            }
        }
    }
    if !rtcFound { r.add("INFO", "WebRTC 保存策略", "未读取到 disable_non_proxied_udp 策略；不会自动修改浏览器。") }
    if !config.apps.isEmpty {
        let agentDirs = [FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/LaunchAgents"), URL(fileURLWithPath:"/Library/LaunchAgents"), URL(fileURLWithPath:"/Library/LaunchDaemons")]
        for dir in agentDirs {
            guard let files = try? FileManager.default.contentsOfDirectory(at:dir,includingPropertiesForKeys:nil) else { continue }
            for file in files where file.pathExtension == "plist" {
                guard let data = try? Data(contentsOf:file), let root = (try? PropertyListSerialization.propertyList(from:data,format:nil)) as? [String:Any] else { continue }
                let program = root["Program"] as? String ?? (root["ProgramArguments"] as? [String])?.first ?? ""
                if config.apps.contains(where: { program.hasPrefix($0.path + "/") }) {
                    r.settings["launchd/" + file.path] = digest(String(decoding:data,as:UTF8.self))
                    r.add("UNVERIFIED","专属 launchd 配置", "\(file.lastPathComponent)：已关联选定应用；本版只读监测，未授权停止服务或包外进程。")
                }
            }
        }
    }
    if !config.chromePreferences.isEmpty {
        do {
            let url = URL(fileURLWithPath: config.chromePreferences)
            let attributes = try FileManager.default.attributesOfItem(atPath: url.path)
            guard (attributes[.size] as? NSNumber)?.intValue ?? Int.max < 32*1024*1024 else { throw GuardError(message: "Chrome Preferences 过大。") }
            let data = try Data(contentsOf: url)
            let root = try JSONSerialization.jsonObject(with: data) as? [String:Any]
            guard let intl = root?["intl"] as? [String:Any], let lang = intl["accept_languages"] as? String else { throw GuardError(message: "所选配置中缺少 intl.accept_languages。") }
            r.settings["Chrome/languages"] = lang
            r.add("OK", "Chrome 资料", "所选 Preferences 的保存语言：\(lang)；不是运行中浏览器证明。")
        } catch { r.add("BLOCK", "Chrome 资料", "无法读取所选 Preferences。") }
    }
}
struct AuditDependencies {
    var readSnapshot: (Config, inout Report) -> Void
    var readListeners: (Int) throws -> [Listener]
    var request: (String) throws -> String
    static let live = AuditDependencies(readSnapshot:systemSnapshot,readListeners:listeners,request:{ try proxied($0) })
}
func audit(_ config: Config, online: Bool, observation: Observation = Observation(), baseline: Baseline? = nil, endpoint: String? = nil, dependencies: AuditDependencies = .live) -> Report {
    var r = Report(); dependencies.readSnapshot(config, &r)
    if let site = endpoint { r.add("OK", "1.2.8 检测策略", "本轮 \(site) · 请求超时 8 秒 · 11 秒起始间隔 · 不重叠，不直连回退。") }
    var relayAvailable = false
    for port in [7890,20808] {
        do {
            let found = try dependencies.readListeners(port)
            if found.count != 1 { r.add("BLOCK", "本机端口 \(port)", found.isEmpty ? "未发现本机可用 LISTEN；不会启动任何应用或中继。" : "发现多个监听所有者，身份不明确。"); continue }
            r.core[port == 7890 ? "clash" : "relay"] = true
            let owner = found[0]
            r.settings["listener/\(port)/path"] = owner.path; r.settings["listener/\(port)/sha256"] = owner.hash
            r.add("OK", "本机端口 \(port)", "LISTEN · PID \(owner.pid) · \(owner.path)；路径及 SHA-256 纳入基准。")
            if port == 20808 { relayAvailable = true }
        } catch { r.add("BLOCK", "本机端口 \(port)", "无法确认监听身份或二进制摘要。") }
    }
    if online && relayAvailable {
        r.networkChecked = true
        let urls = endpoint.map { [$0] } ?? ["https://checkip.amazonaws.com", "https://api.ipify.org"]
        r.endpoint = endpoint ?? "setup-sequential-dual"
        // v1.2.8: setup sequentially checks both, monitoring checks ONE endpoint.
        for url in urls {
            do {
                guard let ip = publicIP(try dependencies.request(url)) else { throw GuardError(message: "Invalid public IP response") }
                r.addresses.append(ip)
            } catch let error as ProbeError {
                if error.code == 28 || error.code == 124 { r.timeoutCount += 1 }
            } catch {}
        }
        let expected = config.expectedIP.isEmpty ? baseline?.ip ?? "" : config.expectedIP
        let changes = classify(expected: expected, addresses: r.addresses)
        for text in changes { r.add("BLOCK", "公网出口", text); r.confirmedChange = true; r.exitMismatch = true }
        r.networkConfirmed = r.addresses.count == urls.count && changes.isEmpty
        r.core["ip"] = r.networkConfirmed
        if r.networkConfirmed { r.add("OK", "公网出口", "\(r.addresses[0]) · \(urls.count == 2 ? "首次双站一致" : "本轮单站检测") · 请求强制经过 20808。") }
        else if r.addresses.count < urls.count { r.add("BLOCK", "公网出口未确认", "至少一个 HTTPS 检测站失败或响应无效；建议暂停使用。失败不等于已证实泄漏。") }
        if r.networkConfirmed && endpoint == nil {
            do {
                let text = try dependencies.request("https://ipinfo.io/\(r.addresses[0])/json")
                guard let meta = try JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String:Any], let org = meta["org"] as? String, let rawIP = meta["ip"] as? String, publicIP(rawIP) == r.addresses[0] else { throw GuardError(message: "归属地响应无效。") }
                let place = [meta["city"],meta["region"],meta["country"]].compactMap { $0 as? String }.joined(separator: " / ")
                let matched = config.expectedOrg.isEmpty || org.range(of: config.expectedOrg, options: .caseInsensitive) != nil
                r.add(matched ? "OK" : "BLOCK", "运营商 / 归属地", "\(org) · \(place)；数据库信息不证明住宅属性。")
                r.core["org"] = matched && !org.isEmpty
                r.settings["exitOrg"] = org
            } catch { r.add(config.expectedOrg.isEmpty ? "UNVERIFIED" : "BLOCK", "运营商 / 归属地", "ipinfo 查询未确认，可能限流或代理异常；没有改走直连。") }
        }
    } else { r.add("BLOCK", "公网出口", online ? "20808 未就绪，已跳过外网探测。" : "离线模式：未发出任何外网探测。") }
    if endpoint != nil, let b = baseline, let org = b.org ?? b.settings["exitOrg"], r.networkConfirmed, r.addresses.first == b.ip {
        r.settings["exitOrg"] = org
        let matched = !org.isEmpty && (config.expectedOrg.isEmpty || org.range(of:config.expectedOrg,options:.caseInsensitive) != nil)
        r.core["org"] = matched
        r.add(matched ? "OK" : "BLOCK","运营商 / ASN", "当前出口仍为受信 IP，复用已核验基准：" + org + "；本轮没有重新查询运营商数据库。")
    }
    if config.privacyVerificationEnabled != true {
        r.add("INFO", "DNS / WebRTC 实际泄漏", "未启用内置辅助核验；外部浏览器核验状态未由本工具验证，不参与五项核心准入。")
    } else if observation.at.timeIntervalSinceNow > -300 && r.networkConfirmed {
        if observation.webrtc.isEmpty { r.add("INFO", "WebRTC 实际观察", "没有本次浏览器观察值。") }
        else {
            let bad = observation.webrtc.filter { publicIP($0) != r.addresses[0] }
            r.add(bad.isEmpty ? "OK" : "WARNING", "WebRTC 实际观察", bad.isEmpty ? "管理员提交的公网候选 IP 与出口一致；不等于所有流量安全。" : "观察值含无效、私网或不同于代理出口的 IP。")
        }
        if observation.dns.isEmpty || config.expectedDNS.isEmpty { r.add("INFO", "DNS 实际观察", "需同时填写本次观察的解析器 IP 和预期解析器白名单。") }
        else {
            let bad = observation.dns.filter { publicIP($0) == nil || !config.expectedDNS.contains($0) }
            r.add(bad.isEmpty ? "OK" : "WARNING", "DNS 实际观察", bad.isEmpty ? "管理员提交的解析器 IP 在预期白名单内；仅对本次观察负责。" : "观察解析器不在白名单内或不是有效公网 IP。")
        }
    } else {
        r.add("INFO", "DNS / WebRTC 实际泄漏", "原项目没有实测引擎。本版检查保存设置并支持本次浏览器观察值核对；未提供或已超过 5 分钟，不宣称安全。")
    }
    for app in config.apps {
        do {
            let bundle = Bundle(path: app.path)
            guard bundle?.bundleIdentifier == app.bundleID, try fileHash(app.executable) == app.hash else { throw GuardError(message: "应用身份变化") }
            r.settings["app/\(app.path)"] = app.hash
            r.add("OK", "保护应用身份", URL(fileURLWithPath: app.path).lastPathComponent + " · Bundle ID / SHA-256 一致；不会启动应用。")
        } catch { r.add("BLOCK", "保护应用身份", "应用版本或身份改变，需重新选择并确认。") }
    }
    if let b = baseline {
        if b.device != (Host.current().localizedName ?? "") || b.uid != getuid() { r.add("BLOCK", "基准设备", "基准属于其他设备或用户，必须重新捕获。") }
        for (key,value) in b.settings where r.settings[key] != value { r.add("BLOCK", "基准漂移", key + " 与确认基准不同。"); r.confirmedChange = true }
    } else { r.add("WARNING", "首次使用", "尚未确认本设备基准。确认并保存后将固化 IP、运营商与时区；辅助项不阻塞核心判定。") }
    return r
}
func loadBaseline() -> Baseline? { try? JSONDecoder().decode(Baseline.self, from: Data(contentsOf: baselineURL)) }
func trustedConfiguration(_ r: Report, config: Config) throws -> Config {
    guard r.coreReady, r.addresses.count == 2, r.addresses[0] == r.addresses[1],
          let ip = r.addresses.first, let org = r.settings["exitOrg"], !org.isEmpty,
          let zone = r.settings["timezone"], TimeZone(identifier:zone) != nil else {
        throw GuardError(message:"五项核心检测或双站一致性未通过，拒绝保存基准。")
    }
    var trusted = config; trusted.expectedIP = ip; trusted.expectedOrg = org; trusted.expectedTimezone = zone
    return trusted
}
func reportAfterSave(_ r: Report) -> Report {
    var refreshed = r
    refreshed.rows.removeAll { $0.name == "首次使用" }
    refreshed.add("OK","受信基准已保存","当前 IP、运营商 / ASN 与时区已固化；辅助项不影响核心准入。")
    return refreshed
}
@discardableResult
func saveBaseline(_ r: Report, config: Config, directory: URL = dataRoot) throws -> Config {
    let trusted = try trustedConfiguration(r,config:config)
    let b = Baseline(capturedAt:r.time,device:Host.current().localizedName ?? "",uid:getuid(),ip:trusted.expectedIP,settings:r.settings,org:trusted.expectedOrg,timezone:trusted.expectedTimezone)
    let cfg = directory.appendingPathComponent("config.json"), base = directory.appendingPathComponent("baseline.json")
    let previousConfig = try? Data(contentsOf:cfg), previousBaseline = try? Data(contentsOf:base)
    do { try atomicJSON(b,to:base); try atomicJSON(trusted,to:cfg) }
    catch {
        if let data = previousBaseline { try? data.write(to:base,options:.atomic) } else { try? FileManager.default.removeItem(at:base) }
        if let data = previousConfig { try? data.write(to:cfg,options:.atomic) }
        throw error
    }
    return trusted
}
func auditLog(_ r: Report) throws {
    try FileManager.default.createDirectory(at: dataRoot, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    let path = dataRoot.appendingPathComponent("audit.jsonl")
    if !FileManager.default.fileExists(atPath: path.path) { FileManager.default.createFile(atPath: path.path, contents: nil, attributes: [.posixPermissions: 0o600]) }
    let f = try FileHandle(forWritingTo: path); defer { try? f.close() }; try f.seekToEnd()
    try f.write(contentsOf: JSONEncoder().encode(r) + Data([10]))
}
func selectApp(_ url: URL) throws -> AppTarget {
    let canonical = url.resolvingSymlinksInPath()
    guard canonical.pathExtension == "app", !canonical.path.hasPrefix("/System/"), canonical.path != Bundle.main.bundlePath,
          let bundle = Bundle(url: canonical), let id = bundle.bundleIdentifier, let exe = bundle.executableURL?.resolvingSymlinksInPath(), exe.path.hasPrefix(canonical.path + "/") else { throw GuardError(message: "请选择非系统的独立 .app，不能选择本工具。") }
    return AppTarget(path: canonical.path, executable: exe.path, bundleID: id, hash: try fileHash(exe.path))
}
struct ObservedProcess: Equatable { var pid: Int32; var uid: UInt32; var start: String; var path: String }
func processScope(_ apps: [AppTarget]) -> [ObservedProcess] {
    let text = command("/bin/ps", ["-ww", "-axo", "pid=,uid=,lstart=,comm="]).output
    return text.split(separator: "\n").compactMap { line in
        let fields = line.split(maxSplits: 7, omittingEmptySubsequences: true, whereSeparator: { $0 == " " || $0 == "\t" })
        guard fields.count == 8, let pid = Int32(fields[0]), let uid = UInt32(fields[1]), uid == getuid(), pid != getpid() else { return nil }
        let path = String(fields[7]).trimmingCharacters(in: .whitespaces); let start = fields[2...6].joined(separator: " ")
        guard apps.contains(where: { path.hasPrefix($0.path + "/") }) else { return nil }
        return ObservedProcess(pid: pid, uid: uid, start: start, path: path)
    }
}
func emergencyStop(_ config: Config) -> String {
    var verified: [AppTarget] = []; var errors: [String] = []
    for target in config.apps {
        if let fresh = try? selectApp(URL(fileURLWithPath: target.path)), fresh.hash == target.hash, fresh.bundleID == target.bundleID { verified.append(target) }
        else { errors.append("\(URL(fileURLWithPath: target.path).lastPathComponent) 身份变化，拒绝扩大清理。") }
    }
    var killed = 0
    for _ in 0..<4 {
        let observed = processScope(verified)
        if observed.isEmpty { break }
        for p in observed {
            if processScope(verified).contains(p) {
                if kill(p.pid, SIGKILL) == 0 { killed += 1 } else { errors.append("PID \(p.pid) 无法结束。") }
            }
        }
        Thread.sleep(forTimeInterval: 0.15)
    }
    return "已尝试关闭 \(killed) 个已验证应用包内进程；剩余 \(processScope(verified).count)。" + errors.joined(separator: "\n") + "\n范围不含包外守护、共享服务、VM/容器；不代表网络隔离。"
}
func selfTest() -> Int32 {
    func require(_ condition: Bool, _ name: String) { if !condition { print("[FAIL] " + name); exit(1) }; print("[OK] " + name) }
    require(publicIP("8.8.8.8") == "8.8.8.8", "public IPv4")
    require(publicIP("127.0.0.1") == nil && publicIP("192.168.1.2") == nil && publicIP("100.64.0.1") == nil, "local/CGNAT rejected")
    require(publicIP("2606:4700:4700::1111") != nil && publicIP("::1") == nil && publicIP("fc00::1") == nil, "IPv6 validation")
    require(classify(expected: "8.8.8.8", addresses: ["8.8.8.8","1.1.1.1"]).count == 2, "mismatch and baseline drift")
    require(classify(expected: "", addresses: ["8.8.8.8","8.8.8.8"]).isEmpty, "initial dual-endpoint agreement")
    var r = Report(); r.add("UNVERIFIED", "privacy", "unknown")
    require(r.blocked, "missing core measurements never green")
    for (key,_) in Report.criteria { r.core[key] = true }
    require(!r.blocked, "unmeasured privacy does not veto verified core")
    r.networkConfirmed = false
    do { try saveBaseline(r, config: Config()); require(false, "reject invalid capture") } catch { require(true, "reject invalid capture before writes") }
    require(processScope([]).isEmpty, "empty scope never broad-kills")
    print("[OK] No network probes, application launch, signals or production baseline writes in self-test.")
    return 0
}

// Direct port of v1.2.8 AutomaticProtectionPolicy: no local/privacy warning input.
struct AutomaticPolicy {
    var consecutiveTimeouts = 0
    var executed = false
    var eligible = false
    mutating func observe(healthy: Bool, mismatch: Bool, timeouts: Int) {
        if healthy { consecutiveTimeouts = 0; executed = false; eligible = false; return }
        consecutiveTimeouts = timeouts > 0 ? consecutiveTimeouts + 1 : 0
        eligible = mismatch || consecutiveTimeouts >= 2
    }
    mutating func claim(enabled: Bool) -> Bool {
        if !enabled || executed || !eligible { return false }
        executed = true; return true
    }
}
struct NetworkSchedule {
    var nextStart: TimeInterval = 0
    func due(now: TimeInterval, inFlight: Bool) -> Bool { !inFlight && now >= nextStart }
    mutating func started(now: TimeInterval) { nextStart = now + 11 }
}
