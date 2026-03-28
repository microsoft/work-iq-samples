//
//  A2A_ChatApp.swift
//  A2A Chat
//
//  Created by Tolga Kilicli on 3/20/26.
//

import SwiftUI
import MSAL

@main
struct A2A_ChatApp: App {
    @State private var authService = AuthService()

    var body: some Scene {
        WindowGroup {
            ContentView(authService: authService)
                .onOpenURL { url in
                    // Forward broker callback to MSAL
                    MSALPublicClientApplication.handleMSALResponse(
                        url,
                        sourceApplication: nil
                    )
                }
        }
    }
}
