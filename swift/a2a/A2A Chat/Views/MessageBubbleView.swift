//
//  MessageBubbleView.swift
//  A2A Chat
//
//  Created by Tolga Kilicli on 3/20/26.
//

import SwiftUI

struct MessageBubbleView: View {
    @Bindable var message: ChatMessage

    var body: some View {
        HStack {
            if message.isUser { Spacer(minLength: 60) }

            Group {
                if message.isUser {
                    Text(message.text)
                } else {
                    Text(markdownAttributedString)
                }
            }
            .padding(12)
            .background(message.isUser ? Color.blue : Color(.systemGray5))
            .foregroundStyle(message.isUser ? .white : .primary)
            .clipShape(RoundedRectangle(cornerRadius: 16))
            .textSelection(.enabled)

            if !message.isUser { Spacer(minLength: 60) }
        }
    }

    private var markdownAttributedString: AttributedString {
        renderMarkdown(text: message.text, isComplete: message.isComplete)
    }
}

/// Render text as an AttributedString with markdown formatting.
/// Complete messages use full markdown parsing (handles paragraphs, lists, etc.)
/// Streaming chunks use inline-only (handles partial bold/italic/links)
func renderMarkdown(text: String, isComplete: Bool) -> AttributedString {
    if isComplete {
        if let result = try? AttributedString(markdown: text, options: .init(interpretedSyntax: .full)) {
            return result
        }
    }
    if let result = try? AttributedString(markdown: text, options: .init(interpretedSyntax: .inlineOnlyPreservingWhitespace)) {
        return result
    }
    return AttributedString(text)
}
