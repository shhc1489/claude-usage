import SwiftUI
import AppKit

// MARK: - Credentials (shared with Claude Code via the login keychain)

struct Credentials {
    static let service = "Claude Code-credentials"
    static let clientID = "9d1c250a-e61b-44d9-88ed-5944d1962f5e"
    static let tokenURLs = [
        URL(string: "https://platform.claude.com/v1/oauth/token")!,
        URL(string: "https://console.anthropic.com/v1/oauth/token")!,
    ]

    var root: [String: Any]
    var account: String

    var oauth: [String: Any] { root["claudeAiOauth"] as? [String: Any] ?? [:] }
    var accessToken: String? { oauth["accessToken"] as? String }
    var refreshToken: String? { oauth["refreshToken"] as? String }
    var plan: String? { oauth["subscriptionType"] as? String }
    var isExpired: Bool {
        guard let ms = oauth["expiresAt"] as? Double else { return false }
        return Date(timeIntervalSince1970: ms / 1000) < Date().addingTimeInterval(60)
    }

    static func load() throws -> Credentials {
        let json = try runSecurity(["find-generic-password", "-s", service, "-w"])
        guard let data = json.data(using: .utf8),
              let root = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { throw AppError("Couldn't parse Claude Code credentials") }
        let attrs = (try? runSecurity(["find-generic-password", "-s", service])) ?? ""
        let account = attrs.components(separatedBy: "\n")
            .first { $0.contains("\"acct\"") }
            .flatMap { $0.components(separatedBy: "=\"").last?.dropLast() }
            .map(String.init) ?? NSUserName()
        return Credentials(root: root, account: account)
    }

    /// Refreshes the token the same way Claude Code does and writes it back,
    /// so Claude Code keeps working with the rotated refresh token.
    mutating func refresh() async throws {
        guard let rt = refreshToken else { throw AppError("No refresh token — run `claude` and log in") }
        let body = try JSONSerialization.data(withJSONObject: [
            "grant_type": "refresh_token", "refresh_token": rt, "client_id": Self.clientID,
        ])
        var lastError: Error = AppError("Token refresh failed")
        for url in Self.tokenURLs {
            var req = URLRequest(url: url)
            req.httpMethod = "POST"
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.httpBody = body
            do {
                let (data, resp) = try await URLSession.shared.data(for: req)
                guard (resp as? HTTPURLResponse)?.statusCode == 200,
                      let tok = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                      let access = tok["access_token"] as? String
                else { lastError = AppError("Token refresh rejected — run `claude` and log in"); continue }
                var o = oauth
                o["accessToken"] = access
                if let newRT = tok["refresh_token"] as? String { o["refreshToken"] = newRT }
                if let exp = tok["expires_in"] as? Double {
                    o["expiresAt"] = (Date().timeIntervalSince1970 + exp) * 1000
                }
                root["claudeAiOauth"] = o
                let out = String(data: try JSONSerialization.data(withJSONObject: root), encoding: .utf8)!
                _ = try Self.runSecurity(["add-generic-password", "-U", "-a", account, "-s", Self.service, "-w", out])
                return
            } catch { lastError = error }
        }
        throw lastError
    }

    @discardableResult
    static func runSecurity(_ args: [String]) throws -> String {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        p.arguments = args
        let out = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        try p.run()
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else {
            throw AppError("Claude Code credentials not found in keychain — run `claude` and log in")
        }
        return String(data: data, encoding: .utf8)!.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

struct AppError: LocalizedError {
    let message: String
    init(_ m: String) { message = m }
    var errorDescription: String? { message }
}

// MARK: - Usage API

struct Window: Decodable {
    let utilization: Double?
    let resets_at: String?

    var resetDate: Date? {
        guard let s = resets_at else { return nil }
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        return f.date(from: s)
    }
}

struct Usage: Decodable {
    let five_hour: Window?
    let seven_day: Window?
    let seven_day_opus: Window?
    let seven_day_sonnet: Window?
}

@MainActor
final class UsageModel: ObservableObject {
    @Published var usage: Usage?
    @Published var error: String?
    @Published var lastUpdated: Date?
    @Published var plan: String?
    @Published var loading = false
    private var timer: Timer?

    init() {
        Task { await refresh() }
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            Task { await self?.refresh() }
        }
    }

    func refresh() async {
        loading = true
        defer { loading = false }
        do {
            var creds = try Credentials.load()
            plan = creds.plan
            if creds.isExpired { try await creds.refresh() }
            var (data, status) = try await fetch(token: creds.accessToken)
            if status == 401 {
                try await creds.refresh()
                (data, status) = try await fetch(token: creds.accessToken)
            }
            guard status == 200 else { throw AppError("Usage request failed (HTTP \(status))") }
            usage = try JSONDecoder().decode(Usage.self, from: data)
            lastUpdated = Date()
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }

    private func fetch(token: String?) async throws -> (Data, Int) {
        var req = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
        req.setValue("Bearer \(token ?? "")", forHTTPHeaderField: "Authorization")
        req.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        let (data, resp) = try await URLSession.shared.data(for: req)
        return (data, (resp as? HTTPURLResponse)?.statusCode ?? 0)
    }

    var menuTitle: String {
        guard let u = usage?.five_hour?.utilization else { return error == nil ? "…" : "!" }
        return "\(Int(u.rounded()))%"
    }
}

// MARK: - UI

struct UsageRow: View {
    let title: String
    let window: Window

    var body: some View {
        let pct = window.utilization ?? 0
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(title).font(.system(size: 12, weight: .medium))
                Spacer()
                Text("\(Int(pct.rounded()))%").font(.system(size: 12, weight: .semibold)).monospacedDigit()
            }
            ProgressView(value: min(pct, 100), total: 100)
                .tint(pct >= 90 ? .red : pct >= 70 ? .orange : .accentColor)
            if let d = window.resetDate {
                Text("Resets \(Self.format(d))")
                    .font(.system(size: 11)).foregroundStyle(.secondary)
            }
        }
    }

    static func format(_ d: Date) -> String {
        let df = DateFormatter()
        df.dateFormat = Calendar.current.isDateInToday(d) ? "h:mm a"
            : Calendar.current.isDateInTomorrow(d) ? "'tomorrow' h:mm a" : "EEE d MMM, h:mm a"
        let secs = max(0, Int(d.timeIntervalSinceNow))
        let h = secs / 3600, m = (secs % 3600) / 60
        let rel = h >= 24 ? "\(h / 24)d \(h % 24)h" : h > 0 ? "\(h)h \(m)m" : "\(m)m"
        return "\(df.string(from: d)) (in \(rel))"
    }
}

struct PopoverView: View {
    @ObservedObject var model: UsageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("Claude usage").font(.system(size: 13, weight: .semibold))
                if let p = model.plan {
                    Text(p.capitalized).font(.system(size: 10, weight: .medium))
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(.quaternary, in: Capsule())
                }
                Spacer()
            }
            if let e = model.error {
                Text(e).font(.system(size: 11)).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
            }
            if let u = model.usage {
                if let w = u.five_hour { UsageRow(title: "Current session (5h)", window: w) }
                if let w = u.seven_day { UsageRow(title: "Weekly — all models", window: w) }
                if let w = u.seven_day_opus, w.utilization != nil { UsageRow(title: "Weekly — Opus", window: w) }
                if let w = u.seven_day_sonnet, w.utilization != nil { UsageRow(title: "Weekly — Sonnet", window: w) }
            }
            Divider()
            HStack {
                if let t = model.lastUpdated {
                    Text("Updated \(t.formatted(date: .omitted, time: .shortened))")
                        .font(.system(size: 10)).foregroundStyle(.secondary)
                }
                Spacer()
                Button(model.loading ? "Refreshing…" : "Refresh") { Task { await model.refresh() } }
                    .disabled(model.loading)
                Button("Quit") { NSApp.terminate(nil) }
            }
            .controlSize(.small)
        }
        .padding(14)
        .frame(width: 280)
    }
}

@main
struct ClaudeUsageApp: App {
    @StateObject private var model = UsageModel()

    var body: some Scene {
        MenuBarExtra {
            PopoverView(model: model)
        } label: {
            HStack(spacing: 3) {
                Image(systemName: "gauge.with.dots.needle.50percent")
                Text(model.menuTitle).monospacedDigit()
            }
        }
        .menuBarExtraStyle(.window)
    }
}
