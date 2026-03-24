# A2A Chat

A SwiftUI iOS/iPadOS app for chatting with [Microsoft Work IQ](https://aka.ms/workiq) using the [A2A (Agent-to-Agent) protocol v0.3](https://a2a-protocol.org). The AI assistant can access and reason about your Microsoft 365 data — emails, Teams chats, OneDrive documents, and more.

## Features

- **Microsoft 365 sign-in** via MSAL (device code / interactive)
- **A2A v0.3 protocol** over JSON-RPC transport
- **Streaming responses** via SSE with real-time token-by-token display
- **Multi-turn conversations** with persistent context
- **Markdown rendering** in agent responses
- **Automatic token refresh** for long sessions

## Prerequisites

- **Xcode 26+** (Swift 6.0)
- iOS/iPadOS 26+ device or simulator
- A Microsoft 365 account with access to Work IQ
- An Azure AD app registration (see [Setup](#azure-ad-app-registration) below)

## Azure AD App Registration

The included scripts automate app registration. Pick whichever matches your environment:

### macOS / Linux

```bash
# Requires Azure CLI — install via: brew install azure-cli
az login
./setup-app-registration.sh
```

### Windows

```powershell
# Requires Azure CLI
az login
.\setup-app-registration.ps1
```

Both scripts will:

1. Create an app registration named **A2A Chat**
2. Configure the redirect URI (`msauth.app.blueglass.A2A-Chat://auth`)
3. Add the required Microsoft Graph delegated permissions:
   - `User.Read`, `Sites.Read.All`, `Mail.Read`, `People.Read.All`
   - `OnlineMeetingTranscript.Read.All`, `Chat.Read`, `ChannelMessage.Read.All`, `ExternalItem.Read.All`
4. Grant admin consent
5. Print the **App (client) ID**

Update the `clientId` constant in `A2A Chat/Services/AuthService.swift` with the printed App ID.

## Build & Run

1. Open `A2A Chat.xcodeproj` in Xcode.
2. Swift packages (MSAL, A2AClient) will resolve automatically.
3. Select an iOS simulator or device and run.

## Architecture

```
A2A Chat/
├── A2A_ChatApp.swift              # App entry point, MSAL callback handling
├── Models/
│   └── ChatMessage.swift          # Chat message data model
├── Services/
│   ├── AuthService.swift          # MSAL auth (sign-in, token refresh, sign-out)
│   └── A2AService.swift           # Work IQ A2A client (streaming & sync)
└── Views/
    ├── ContentView.swift          # Root view — routes between Welcome and Chat
    ├── WelcomeView.swift          # Sign-in screen
    ├── ChatView.swift             # Chat interface with message input
    └── MessageBubbleView.swift    # Message bubble with markdown support
```

### Dependencies

| Package | Source | Purpose |
|---------|--------|---------|
| [MSAL](https://github.com/AzureAD/microsoft-authentication-library-for-objc) | AzureAD | Microsoft 365 authentication |
| [A2AClient](https://github.com/tolgaki/a2a-client-swift) (1.0.12+) | tolgaki | A2A v0.3 protocol client for Work IQ |

## License

[MIT](../LICENSE)
