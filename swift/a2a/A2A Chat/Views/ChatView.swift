//
//  ChatView.swift
//  A2A Chat
//
//  Created by Tolga Kilicli on 3/20/26.
//

import SwiftUI

struct ChatView: View {
    var authService: AuthService
    @State private var a2aService: A2AService
    @State private var messages: [ChatMessage] = []
    @State private var inputText = ""
    @State private var isWaiting = false

    init(authService: AuthService) {
        self.authService = authService
        self._a2aService = State(initialValue: A2AService(authService: authService))
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 12) {
                            ForEach(messages) { message in
                                MessageBubbleView(message: message)
                            }
                            if isWaiting {
                                HStack {
                                    ProgressView()
                                        .padding(.leading)
                                    Spacer()
                                }
                                .id("loading")
                            }
                        }
                        .padding()
                    }
                    .onChange(of: messages.count) {
                        scrollToBottom(proxy: proxy)
                    }
                    .onChange(of: isWaiting) {
                        scrollToBottom(proxy: proxy)
                    }
                    .onChange(of: messages.last?.text) {
                        scrollToBottom(proxy: proxy)
                    }
                }

                Divider()

                HStack(spacing: 12) {
                    TextField("Message", text: $inputText, axis: .vertical)
                        .textFieldStyle(.plain)
                        .lineLimit(1...5)
                        .padding(12)
                        .background(Color(.systemGray6))
                        .clipShape(RoundedRectangle(cornerRadius: 20))

                    Button(action: sendMessage) {
                        Image(systemName: "paperplane.fill")
                            .font(.title3)
                            .foregroundStyle(canSend ? .blue : .gray)
                    }
                    .disabled(!canSend)
                }
                .padding(.horizontal)
                .padding(.vertical, 8)
            }
            .onTapGesture {
                UIApplication.shared.sendAction(#selector(UIResponder.resignFirstResponder), to: nil, from: nil, for: nil)
            }
            .navigationTitle("A2A Chat")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Sign Out") {
                        a2aService.reset()
                        authService.signOut()
                    }
                    .font(.caption)
                }
            }
        }
    }

    private var canSend: Bool {
        !inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !isWaiting
    }

    private func sendMessage() {
        let text = inputText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return }

        messages.append(ChatMessage(text: text, isUser: true))
        inputText = ""
        isWaiting = true

        Task {
            var agentMessage: ChatMessage? = nil

            do {
                try await a2aService.sendStreaming(text) { streamedText in
                    if let msg = agentMessage {
                        msg.text = streamedText
                    } else {
                        let msg = ChatMessage(text: streamedText, isUser: false)
                        agentMessage = msg
                        messages.append(msg)
                        isWaiting = false
                    }
                }
                if let msg = agentMessage {
                    msg.isComplete = true
                } else {
                    messages.append(ChatMessage(text: "[No response]", isUser: false))
                }
            } catch {
                if let msg = agentMessage {
                    msg.text += "\n\nError: \(error.localizedDescription)"
                } else {
                    messages.append(ChatMessage(text: "Error: \(error.localizedDescription)", isUser: false))
                }
            }
            isWaiting = false
        }
    }

    private func scrollToBottom(proxy: ScrollViewProxy) {
        withAnimation {
            if isWaiting {
                proxy.scrollTo("loading", anchor: .bottom)
            } else if let last = messages.last {
                proxy.scrollTo(last.id, anchor: .bottom)
            }
        }
    }
}
