//
//  AuthService.swift
//  A2A Chat
//
//  Created by Tolga Kilicli on 3/20/26.
//

import Foundation
import MSAL
import os.log

private let log = Logger(subsystem: "app.blueglass.A2A-Chat", category: "Auth")

@Observable
class AuthService {
    private(set) var isAuthenticated = false
    private(set) var accessToken: String?
    private(set) var userName: String?
    var error: String?

    private var application: MSALPublicClientApplication?
    private var account: MSALAccount?
    private let scopes: [String]

    init() {
        let config = AppConfiguration.load()
        self.scopes = config?.scopes ?? ["api://workiq.svc.cloud.microsoft/.default"]

        guard let config else {
            log.error("Configuration.plist missing or invalid")
            self.error = "Copy Configuration.example.plist to Configuration.plist and set your App ID. See README for setup instructions."
            return
        }

        log.info("MSAL init — clientId=\(config.clientId, privacy: .public) tenant=\(config.tenantId, privacy: .public)")

        do {
            let msalConfig = MSALPublicClientApplicationConfig(clientId: config.clientId)
            let authorityURL = URL(string: "https://login.microsoftonline.com/\(config.tenantId)")!
            msalConfig.authority = try MSALAADAuthority(url: authorityURL)
            if let redirectUri = config.redirectUri {
                msalConfig.redirectUri = redirectUri
            }
            application = try MSALPublicClientApplication(configuration: msalConfig)
        } catch let nsError as NSError {
            log.error("MSAL init failed — \(nsError.domain) \(nsError.code) \(nsError.localizedDescription)")
            self.error = "MSAL setup failed: \(nsError.domain) \(nsError.code) — \(nsError.localizedDescription)"
        }
    }

    func signIn() async {
        guard let application else {
            if error == nil {
                error = "Copy Configuration.example.plist to Configuration.plist and set your App ID. See README for setup instructions."
            }
            return
        }

        error = nil

        // Try silent first; fall back to interactive on any failure.
        if (try? await acquireTokenSilently()) != nil {
            log.info("signIn — silent token acquired")
            return
        }

        guard let rootVC = Self.rootViewController() else {
            log.error("signIn — no window scene / rootVC")
            error = "No window available for sign-in"
            return
        }

        let webParams = MSALWebviewParameters(authPresentationViewController: rootVC)
        webParams.webviewType = .authenticationSession
        webParams.prefersEphemeralWebBrowserSession = true
        let params = MSALInteractiveTokenParameters(scopes: scopes, webviewParameters: webParams)

        do {
            let result = try await application.acquireToken(with: params)
            log.info("signIn — token acquired for \(result.account.username ?? "unknown", privacy: .private(mask: .hash))")
            applyResult(result)
        } catch let nsError as NSError {
            log.error("signIn failed — \(nsError.domain) \(nsError.code) \(nsError.localizedDescription)")
            self.error = nsError.localizedDescription
        }
    }

    func signOut() {
        if let application, let account {
            try? application.remove(account)
        }
        accessToken = nil
        account = nil
        userName = nil
        isAuthenticated = false
        error = nil
    }

    func refreshToken() async -> String? {
        guard application != nil else { return accessToken }
        do {
            _ = try await acquireTokenSilently()
        } catch {
            log.error("refreshToken — silent refresh failed: \(error.localizedDescription)")
            accessToken = nil
            isAuthenticated = false
        }
        return accessToken
    }

    @discardableResult
    private func acquireTokenSilently() async throws -> MSALResult? {
        guard let application, let account else { return nil }
        let params = MSALSilentTokenParameters(scopes: scopes, account: account)
        let result = try await application.acquireTokenSilent(with: params)
        applyResult(result)
        return result
    }

    private func applyResult(_ result: MSALResult) {
        accessToken = result.accessToken
        account = result.account
        userName = result.account.username
        isAuthenticated = true
        error = nil
    }

    private static func rootViewController() -> UIViewController? {
        UIApplication.shared.connectedScenes
            .compactMap { $0 as? UIWindowScene }
            .first?
            .windows.first?
            .rootViewController
    }
}

// MARK: - Configuration

/// All settings loaded from `Configuration.plist` in one shot.
/// `clientId` is required; everything else has a sensible default.
struct AppConfiguration {
    let clientId: String
    let tenantId: String
    let redirectUri: String?
    let scopes: [String]
    let endpoint: URL?

    static func load(bundle: Bundle = .main) -> AppConfiguration? {
        guard
            let path = bundle.path(forResource: "Configuration", ofType: "plist"),
            let dict = NSDictionary(contentsOfFile: path) as? [String: Any]
        else {
            return nil
        }

        guard let clientId = dict.nonPlaceholder("ClientId", placeholder: "YOUR_APP_CLIENT_ID") else {
            return nil
        }

        return AppConfiguration(
            clientId: clientId,
            tenantId: (dict["TenantId"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "common",
            redirectUri: dict.nonPlaceholder("RedirectUri", placeholder: "YOUR_REDIRECT_URI"),
            scopes: (dict["Scopes"] as? [String]).flatMap { $0.isEmpty ? nil : $0 }
                ?? ["api://workiq.svc.cloud.microsoft/.default"],
            endpoint: dict.nonPlaceholder("Endpoint", placeholder: "YOUR_ENDPOINT_URL")
                .flatMap(URL.init(string:))
        )
    }
}

private extension Dictionary where Key == String, Value == Any {
    /// Returns the value for `key` only if it's a non-empty string and not the
    /// listed placeholder. Otherwise nil.
    func nonPlaceholder(_ key: String, placeholder: String) -> String? {
        guard let value = self[key] as? String, !value.isEmpty, value != placeholder else {
            return nil
        }
        return value
    }
}
