//
//  ContentView.swift
//  A2A Chat
//
//  Created by Tolga Kilicli on 3/20/26.
//

import SwiftUI

struct ContentView: View {
    var authService: AuthService

    var body: some View {
        if authService.isAuthenticated {
            ChatView(authService: authService)
        } else {
            WelcomeView(authService: authService)
        }
    }
}
