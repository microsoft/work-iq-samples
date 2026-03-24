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

    private static let clientId = "a668445b-6bb2-40f7-9aa6-87331e80db51"
    private static let redirectUri = "msauth.app.blueglass.A2A-Chat://auth"
    private static let scopes = ["https://graph.microsoft.com/.default"]

    init() {
        guard !Self.clientId.isEmpty else {
            log.warning("clientId is empty, skipping MSAL init")
            return
        }

        log.info("MSAL init — clientId: \(Self.clientId)")
        log.info("MSAL init — redirectUri: \(Self.redirectUri)")

        do {
            let config = MSALPublicClientApplicationConfig(clientId: Self.clientId)
            log.info("MSAL config created")

            let authorityURL = URL(string: "https://login.microsoftonline.com/ca24a1b0-4df5-4b45-8126-22d617eb8f90")!
            config.authority = try MSALAADAuthority(url: authorityURL)
            log.info("MSAL authority set: \(authorityURL.absoluteString)")

            config.redirectUri = Self.redirectUri
            log.info("MSAL redirectUri set: \(Self.redirectUri)")

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

    func signIn() async {
        guard let application else {
            log.error("signIn called but application is nil — error: \(self.error ?? "none")")
            if error == nil {
                error = "Configure clientId in AuthService.swift"
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
                scopes: Self.scopes,
                webviewParameters: webParams
            )
            log.info("signIn — calling acquireToken (scopes: \(Self.scopes.joined(separator: ", ")))")

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
        let params = MSALSilentTokenParameters(scopes: Self.scopes, account: account)
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
