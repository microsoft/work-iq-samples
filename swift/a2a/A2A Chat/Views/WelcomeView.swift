//
//  WelcomeView.swift
//  A2A Chat
//
//  Created by Tolga Kilicli on 3/20/26.
//

import SwiftUI

struct WelcomeView: View {
    var authService: AuthService
    @State private var isSigningIn = false

    var body: some View {
        VStack(spacing: 32) {
            Spacer()

            Image(systemName: "bubble.left.and.bubble.right.fill")
                .font(.system(size: 64))
                .foregroundStyle(.blue)

            Text("A2A Chat")
                .font(.largeTitle.bold())

            Text("AI-powered assistant\nfor Microsoft 365")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)

            Spacer()

            Button {
                isSigningIn = true
                Task {
                    await authService.signIn()
                    isSigningIn = false
                }
            } label: {
                HStack(spacing: 12) {
                    if isSigningIn {
                        ProgressView()
                            .tint(.white)
                    }
                    Image(systemName: "person.badge.key.fill")
                    Text("Sign in with Microsoft 365")
                        .fontWeight(.semibold)
                }
                .frame(maxWidth: .infinity)
                .padding()
                .background(.blue)
                .foregroundStyle(.white)
                .clipShape(RoundedRectangle(cornerRadius: 12))
            }
            .disabled(isSigningIn)
            .padding(.horizontal, 40)

            if let error = authService.error {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .padding(.horizontal)
            }

            Spacer()
                .frame(height: 60)
        }
    }
}
