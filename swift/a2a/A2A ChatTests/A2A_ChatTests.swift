//
//  A2A_ChatTests.swift
//  A2A ChatTests
//
//  Created by Tolga Kilicli on 3/20/26.
//

import Testing
import Foundation
@testable import A2A_Chat

// MARK: - ChatMessage Model Tests

struct ChatMessageTests {

    @Test func initSetsTextAndIsUser() {
        let userMsg = ChatMessage(text: "Hello", isUser: true)
        #expect(userMsg.text == "Hello")
        #expect(userMsg.isUser == true)

        let botMsg = ChatMessage(text: "Hi there", isUser: false)
        #expect(botMsg.text == "Hi there")
        #expect(botMsg.isUser == false)
    }

    @Test func initDefaultsIsCompleteToFalse() {
        let msg = ChatMessage(text: "test", isUser: true)
        #expect(msg.isComplete == false)
    }

    @Test func idIsUniqueAcrossInstances() {
        let msg1 = ChatMessage(text: "a", isUser: true)
        let msg2 = ChatMessage(text: "a", isUser: true)
        #expect(msg1.id != msg2.id)
    }

    @Test func timestampIsSetToApproximatelyNow() {
        let before = Date()
        let msg = ChatMessage(text: "test", isUser: false)
        let after = Date()

        #expect(msg.timestamp >= before)
        #expect(msg.timestamp <= after)
    }

    @Test func textIsMutable() {
        let msg = ChatMessage(text: "original", isUser: false)
        msg.text = "updated"
        #expect(msg.text == "updated")
    }

    @Test func isCompleteIsMutable() {
        let msg = ChatMessage(text: "streaming…", isUser: false)
        #expect(msg.isComplete == false)
        msg.isComplete = true
        #expect(msg.isComplete == true)
    }

    @Test func emptyTextIsAllowed() {
        let msg = ChatMessage(text: "", isUser: true)
        #expect(msg.text == "")
    }

    @Test func textWithSpecialCharacters() {
        let special = "Hello 🌍! Line1\nLine2\t**bold** `code`"
        let msg = ChatMessage(text: special, isUser: false)
        #expect(msg.text == special)
    }
}

// MARK: - BearerTokenAuth Tests

struct BearerTokenAuthTests {

    @Test func authenticateAddsBearerHeader() async throws {
        let auth = BearerTokenAuth(token: "test-token-123")
        let original = URLRequest(url: URL(string: "https://example.com/api")!)

        let authenticated = try await auth.authenticate(request: original)

        #expect(authenticated.value(forHTTPHeaderField: "Authorization") == "Bearer test-token-123")
    }

    @Test func authenticatePreservesOriginalURL() async throws {
        let url = URL(string: "https://example.com/path?q=1")!
        let auth = BearerTokenAuth(token: "abc")
        let original = URLRequest(url: url)

        let authenticated = try await auth.authenticate(request: original)

        #expect(authenticated.url == url)
    }

    @Test func authenticatePreservesExistingHeaders() async throws {
        let auth = BearerTokenAuth(token: "xyz")
        var original = URLRequest(url: URL(string: "https://example.com")!)
        original.setValue("application/json", forHTTPHeaderField: "Content-Type")

        let authenticated = try await auth.authenticate(request: original)

        #expect(authenticated.value(forHTTPHeaderField: "Content-Type") == "application/json")
        #expect(authenticated.value(forHTTPHeaderField: "Authorization") == "Bearer xyz")
    }

    @Test func authenticateHandlesEmptyToken() async throws {
        let auth = BearerTokenAuth(token: "")
        let original = URLRequest(url: URL(string: "https://example.com")!)

        let authenticated = try await auth.authenticate(request: original)

        #expect(authenticated.value(forHTTPHeaderField: "Authorization") == "Bearer ")
    }
}

// MARK: - Configuration Loading Tests
//
// AuthService.loadClientId(), loadRedirectUri(), loadTenantId(), loadScopes()
// and A2AService.loadEndpoint() are all `private static` methods that rely on
// Bundle.main to locate Configuration.plist. This makes them difficult to unit
// test without either:
//   1. Injecting a Bundle parameter (recommended refactoring), or
//   2. Placing a test Configuration.plist in the test bundle.
//
// Below are TODO stubs documenting the cases that should be covered once the
// methods accept a configurable Bundle or dictionary source.

/*
struct ConfigurationLoadingTests {

    // --- AuthService.loadClientId ---

    // TODO: Test that loadClientId returns nil when Configuration.plist is missing.
    //   Refactoring suggestion: `static func loadClientId(from bundle: Bundle) -> String?`
    //   Then pass a test bundle that has no plist.

    // TODO: Test that loadClientId returns nil when ClientId is the placeholder
    //   value "YOUR_APP_CLIENT_ID".

    // TODO: Test that loadClientId returns nil when ClientId is an empty string.

    // TODO: Test that loadClientId returns the value when ClientId is a valid
    //   non-placeholder string (e.g., "00000000-0000-0000-0000-000000000000").

    // --- AuthService.loadRedirectUri ---

    // TODO: Test that loadRedirectUri returns nil for missing plist.
    // TODO: Test that loadRedirectUri returns nil for placeholder "YOUR_REDIRECT_URI".
    // TODO: Test that loadRedirectUri returns nil for empty string.
    // TODO: Test that loadRedirectUri returns the value for a valid URI.

    // --- AuthService.loadTenantId ---

    // TODO: Test that loadTenantId returns nil for missing plist.
    // TODO: Test that loadTenantId returns nil for empty string.
    // TODO: Test that loadTenantId returns the value for a valid tenant ID.

    // --- AuthService.loadScopes ---

    // TODO: Test that loadScopes returns nil for missing plist.
    // TODO: Test that loadScopes returns nil for an empty array.
    // TODO: Test that loadScopes returns the array for a non-empty array.

    // --- A2AService.loadEndpoint ---

    // TODO: Test that loadEndpoint returns nil for missing plist.
    // TODO: Test that loadEndpoint returns nil for placeholder "YOUR_ENDPOINT_URL".
    // TODO: Test that loadEndpoint returns nil for empty string.
    // TODO: Test that loadEndpoint returns nil for an invalid URL string.
    // TODO: Test that loadEndpoint returns the URL for a valid endpoint.
}
*/

// MARK: - Markdown Rendering Tests

struct MarkdownRenderingTests {

    @Test func completedMessageUsesFullMarkdown() {
        let result = renderMarkdown(text: "**bold** text", isComplete: true)
        // Full markdown should parse block-level elements (paragraphs, lists, etc.)
        #expect(result != AttributedString("**bold** text"))
    }

    @Test func streamingMessageUsesInlineMarkdown() {
        let result = renderMarkdown(text: "**bold** text", isComplete: false)
        // Inline-only should still handle bold/italic but not block elements
        #expect(result != AttributedString("**bold** text"))
    }

    @Test func plainTextFallsBackGracefully() {
        let result = renderMarkdown(text: "no markdown here", isComplete: false)
        // Should still produce a valid AttributedString
        #expect(String(result.characters) == "no markdown here")
    }

    @Test func emptyStringReturnsEmptyAttributedString() {
        let result = renderMarkdown(text: "", isComplete: true)
        #expect(result.characters.count == 0)
    }

    @Test func completedMessageWithListsParsesCorrectly() {
        let md = """
        - Item 1
        - Item 2
        - Item 3
        """
        let result = renderMarkdown(text: md, isComplete: true)
        let text = String(result.characters)
        #expect(text.contains("Item 1"))
        #expect(text.contains("Item 2"))
    }
}
