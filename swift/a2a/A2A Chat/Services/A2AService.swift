//
//  A2AService.swift
//  A2A Chat
//
//  Isolated A2A client wrapper. This is the only file that imports the A2A
//  client package — the rest of the app communicates through the simple
//  `sendStreaming` / `send` / `reset()` interface.
//
//  Created by Tolga Kilicli on 3/20/26.
//

import Foundation
import A2AClient
import os.log

private let log = Logger(subsystem: "app.blueglass.A2A-Chat", category: "A2A")

@Observable
class A2AService {
    private let authService: AuthService
    private var contextId: String?
    private let endpoint: URL?

    init(authService: AuthService) {
        self.authService = authService
        self.endpoint = Self.loadEndpoint()
    }

    private static func loadEndpoint() -> URL? {
        guard let path = Bundle.main.path(forResource: "Configuration", ofType: "plist"),
              let dict = NSDictionary(contentsOfFile: path),
              let urlString = dict["Endpoint"] as? String,
              !urlString.isEmpty,
              urlString != "YOUR_ENDPOINT_URL",
              let url = URL(string: urlString) else {
            return nil
        }
        return url
    }

    // MARK: - Public interface

    /// Send a message via streaming. Calls `onText` with accumulated text as chunks arrive.
    func sendStreaming(_ text: String, onText: @escaping (String) -> Void) async throws {
        guard let token = await authService.refreshToken() else {
            throw URLError(.userAuthenticationRequired)
        }

        log.info("Sending streaming message (contextId: \(self.contextId ?? "nil"))")

        let client = try makeClient(token: token)
        let stream = try await client.sendStreamingMessage(text, contextId: contextId)

        var accumulated = ""

        for try await event in stream {
            switch event {
            case .taskStatusUpdate(let update):
                if let ctxId = update.contextId as String?, !ctxId.isEmpty {
                    contextId = ctxId
                }
                if accumulated.isEmpty, let message = update.status.message {
                    let newText = message.textContent
                    if !newText.isEmpty {
                        accumulated = newText
                        onText(accumulated)
                    }
                }
                if update.status.state.isTerminal {
                    return
                }

            case .message(let message):
                if let ctxId = message.contextId, !ctxId.isEmpty {
                    contextId = ctxId
                }
                let newText = message.textContent
                if !newText.isEmpty {
                    accumulated = newText
                    onText(accumulated)
                }

            case .task(let task):
                if !task.contextId.isEmpty {
                    contextId = task.contextId
                }
                if let message = task.status.message {
                    let newText = message.textContent
                    if !newText.isEmpty {
                        accumulated = newText
                        onText(accumulated)
                    }
                }
                if task.isComplete {
                    return
                }

            case .taskArtifactUpdate(let update):
                let chunk = update.artifact.parts.compactMap(\.text).joined()
                if !chunk.isEmpty {
                    accumulated += chunk
                    onText(accumulated)
                }
            }
        }

        if accumulated.isEmpty {
            onText("[No response]")
        }
    }

    /// Non-streaming send via library.
    func send(_ text: String) async throws -> String {
        guard let token = await authService.refreshToken() else {
            throw URLError(.userAuthenticationRequired)
        }

        let client = try makeClient(token: token)
        let response = try await client.sendMessage(text, contextId: contextId)

        switch response {
        case .message(let message):
            contextId = message.contextId
            return message.textContent
        case .task(let task):
            contextId = task.contextId
            return task.status.message?.textContent ?? "[Task \(task.id) — \(task.state.rawValue)]"
        }
    }

    /// Clear conversation context.
    func reset() {
        contextId = nil
    }

    // MARK: - Private

    private func makeClient(token: String) throws -> A2AClient {
        guard let endpoint else {
            throw URLError(.badURL, userInfo: [
                NSLocalizedDescriptionKey: "Endpoint not configured. Set the Endpoint value in Configuration.plist. See README for setup instructions."
            ])
        }

        let auth = BearerTokenAuth(token: token)

        let config = A2AClientConfiguration(
            baseURL: endpoint,
            transportBinding: .jsonRPC,
            protocolVersion: "0.3",
            timeoutInterval: 300,
            authenticationProvider: auth
        )

        return A2AClient(configuration: config)
    }
}

/// Auth provider that adds a bearer token to every request.
private struct BearerTokenAuth: AuthenticationProvider, Sendable {
    let token: String

    func authenticate(request: URLRequest) async throws -> URLRequest {
        var request = request
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        return request
    }
}
