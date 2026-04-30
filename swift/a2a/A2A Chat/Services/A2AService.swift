//
//  A2AService.swift
//  A2A Chat
//
//  Isolated A2A client wrapper. This is the only file that imports the A2A
//  client package — the rest of the app communicates through the simple
//  `sendStreaming` / `send` / `reset()` interface.
//
//  Defaults to the Work IQ Gateway (`https://workiq.svc.cloud.microsoft/a2a/`).
//  Override via the `Endpoint` key in Configuration.plist.
//
//  Created by Tolga Kilicli on 3/20/26.
//

import Foundation
import A2AClient
import os.log

private let log = Logger(subsystem: "app.blueglass.A2A-Chat", category: "A2A")

@Observable
class A2AService {
    private static let defaultEndpoint = URL(string: "https://workiq.svc.cloud.microsoft/a2a/")!

    private let authService: AuthService
    private let endpoint: URL
    private var contextId: String?

    init(authService: AuthService) {
        self.authService = authService
        self.endpoint = AppConfiguration.load()?.endpoint ?? Self.defaultEndpoint
    }

    // MARK: - Public interface

    /// Send a message via streaming. Calls `onText` with accumulated text as chunks arrive.
    ///
    /// A2A v1.0 streaming semantics:
    ///   .task               -> initial submitted task (informational)
    ///   .taskStatusUpdate   -> chain-of-thought OR terminal status; the final
    ///                          event carries citation metadata
    ///   .taskArtifactUpdate -> answer chunks (this is the answer text)
    ///   .message            -> direct message reply (rare in this flow)
    func sendStreaming(_ text: String, onText: @escaping (String) -> Void) async throws {
        let client = try await makeClient()

        log.info("Sending streaming message (contextId: \(self.contextId ?? "nil", privacy: .public))")
        let stream = try await client.sendStreamingMessage(text, contextId: contextId)

        var accumulated = ""

        for try await event in stream {
            switch event {
            case .task(let task):
                applyContextId(task.contextId)
                if task.isComplete { return }

            case .taskStatusUpdate(let update):
                applyContextId(update.contextId)
                if update.status.state.isTerminal { return }

            case .message(let message):
                applyContextId(message.contextId)
                let newText = message.textContent
                if !newText.isEmpty {
                    accumulated = newText
                    onText(accumulated)
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

    /// Non-streaming send. The agent's answer text comes from
    /// `task.artifacts[].parts`. `task.status.message` carries
    /// chain-of-thought / progress and citation metadata, not the answer.
    func send(_ text: String) async throws -> String {
        let client = try await makeClient()
        let response = try await client.sendMessage(text, contextId: contextId)

        switch response {
        case .message(let message):
            contextId = message.contextId
            return message.textContent
        case .task(let task):
            contextId = task.contextId
            let answer = task.artifacts?
                .flatMap(\.parts)
                .compactMap(\.text)
                .joined(separator: "\n") ?? ""
            return answer.isEmpty
                ? "[Task \(task.id) — \(task.state.rawValue)]"
                : answer
        }
    }

    /// Clear conversation context.
    func reset() {
        contextId = nil
    }

    // MARK: - Private

    private func applyContextId(_ id: String?) {
        guard let id, !id.isEmpty else { return }
        contextId = id
    }

    private func makeClient() async throws -> A2AClient {
        guard let token = await authService.refreshToken() else {
            throw URLError(.userAuthenticationRequired)
        }

        let config = A2AClientConfiguration(
            baseURL: endpoint,
            transportBinding: .jsonRPC,
            protocolVersion: "1.0",
            timeoutInterval: 300,
            authenticationProvider: BearerTokenAuth(token: token)
        )
        return A2AClient(configuration: config)
    }
}

/// Auth provider that adds a bearer token to every request.
struct BearerTokenAuth: AuthenticationProvider, Sendable {
    let token: String

    func authenticate(request: URLRequest) async throws -> URLRequest {
        var request = request
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        return request
    }
}
