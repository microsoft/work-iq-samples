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
        let clientId = Self.loadClientId()
        let redirectUri = Self.loadRedirectUri()
        let tenantId = Self.loadTenantId() ?? "common"
        let scopes = Self.loadScopes() ?? ["https://graph.microsoft.com/.default"]
        self.scopes = scopes

        guard let clientId else {
            log.error("Configuration.plist missing or invalid")
            self.error = "Copy Configuration.example.plist to Configuration.plist and set your App ID. See README for setup instructions."
            return
        }

        log.info("MSAL init — clientId: \(clientId)")
        log.info("MSAL init — redirectUri: \(redirectUri ?? "nil")")
        log.info("MSAL init — tenantId: \(tenantId)")
        log.info("MSAL init — scopes: \(scopes.joined(separator: ", "))")

        do {
            let config = MSALPublicClientApplicationConfig(clientId: clientId)
            log.info("MSAL config created")

            let authorityURL = URL(string: "https://login.microsoftonline.com/\(tenantId)")!
            config.authority = try MSALAADAuthority(url: authorityURL)
            log.info("MSAL authority set: \(authorityURL.absoluteString)")

            if let redirectUri {
                config.redirectUri = redirectUri
            }
            log.info("MSAL redirectUri set: \(config.redirectUri ?? "default")")

            log.info("MSAL creating MSALPublicClientApplication...")
            application = try MSALPublicClientApplication(configuration: config)
            log.info("MSAL init succeeded")
        } catch let nsError as NSError {
            log.error("MSAL init failed — domain: \(nsError.domain) code: \(nsError.code)")
            log.error("MSAL init failed — description: \(nsError.localizedDescription)")
            log.error("MSAL init failed — userInfo: \(nsError.userInfo.description)")
            if let underlying = nsError.userInfo[NSUnderlyingErrorKey] as? NSError {
                log.error("MSAL init failed — underlying: domain=\(underlying.domain) code=\(underlying.code) \(underlying.localizedDescription)")
            }
            self.error = "MSAL setup failed: \(nsError.domain) \(nsError.code) — \(nsError.localizedDescription)"
        }
    }

    private static func loadClientId() -> String? {
        guard let path = Bundle.main.path(forResource: "Configuration", ofType: "plist"),
              let dict = NSDictionary(contentsOfFile: path),
              let clientId = dict["ClientId"] as? String,
              !clientId.isEmpty,
              clientId != "YOUR_APP_CLIENT_ID" else {
            return nil
        }
        return clientId
    }

    private static func loadRedirectUri() -> String? {
        guard let path = Bundle.main.path(forResource: "Configuration", ofType: "plist"),
              let dict = NSDictionary(contentsOfFile: path),
              let uri = dict["RedirectUri"] as? String,
              !uri.isEmpty,
              uri != "YOUR_REDIRECT_URI" else {
            return nil
        }
        return uri
    }

    private static func loadTenantId() -> String? {
        guard let path = Bundle.main.path(forResource: "Configuration", ofType: "plist"),
              let dict = NSDictionary(contentsOfFile: path),
              let tenantId = dict["TenantId"] as? String,
              !tenantId.isEmpty else {
            return nil
        }
        return tenantId
    }

    private static func loadScopes() -> [String]? {
        guard let path = Bundle.main.path(forResource: "Configuration", ofType: "plist"),
              let dict = NSDictionary(contentsOfFile: path),
              let scopes = dict["Scopes"] as? [String],
              !scopes.isEmpty else {
            return nil
        }
        return scopes
    }

    func signIn() async {
        guard let application else {
            log.error("signIn called but application is nil — error: \(self.error ?? "none")")
            if error == nil {
                error = "Copy Configuration.example.plist to Configuration.plist and set your App ID. See README for setup instructions."
            }
            return
        }

        error = nil
        log.info("signIn — starting interactive flow")

        do {
            // Try silent first
            if let _ = try? await acquireTokenSilently() {
                log.info("signIn — silent token acquired")
                return
            }

            // Interactive sign-in
            guard let windowScene = UIApplication.shared.connectedScenes.first as? UIWindowScene,
                  let rootVC = windowScene.windows.first?.rootViewController else {
                log.error("signIn — no window scene / rootVC")
                error = "No window available for sign-in"
                return
            }

            let webParams = MSALWebviewParameters(authPresentationViewController: rootVC)
            webParams.webviewType = .authenticationSession
            webParams.prefersEphemeralWebBrowserSession = true
            let params = MSALInteractiveTokenParameters(
                scopes: scopes,
                webviewParameters: webParams
            )
            log.info("signIn — calling acquireToken (scopes: \(self.scopes.joined(separator: ", ")))")

            let result = try await application.acquireToken(with: params)
            log.info("signIn — token acquired for \(result.account.username ?? "unknown")")
            applyResult(result)
        } catch let nsError as NSError {
            log.error("signIn failed — domain: \(nsError.domain) code: \(nsError.code)")
            log.error("signIn failed — \(nsError.localizedDescription)")
            log.error("signIn failed — userInfo: \(nsError.userInfo.description)")
            self.error = nsError.localizedDescription
        }
    }

    func signOut() {
        log.info("signOut")
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
        _ = try? await acquireTokenSilently()
        return accessToken
    }

    private func acquireTokenSilently() async throws -> MSALResult? {
        guard let application, let account else { return nil }
        log.info("acquireTokenSilently — account: \(account.username ?? "unknown")")
        let params = MSALSilentTokenParameters(scopes: scopes, account: account)
        let result = try await application.acquireTokenSilent(with: params)
        log.info("acquireTokenSilently — success")
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
}
