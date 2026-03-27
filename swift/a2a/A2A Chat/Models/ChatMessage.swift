//
//  ChatMessage.swift
//  A2A Chat
//
//  Created by Tolga Kilicli on 3/20/26.
//

import Foundation

@Observable
class ChatMessage: Identifiable {
    let id = UUID()
    var text: String
    let isUser: Bool
    var isComplete = false
    let timestamp = Date()

    init(text: String, isUser: Bool) {
        self.text = text
        self.isUser = isUser
    }
}
