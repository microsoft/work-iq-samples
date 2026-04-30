# A2A Chat (iOS)

A SwiftUI iOS/iPadOS app for chatting with [Microsoft Work IQ](https://aka.ms/workiq) using the [A2A v1.0 (Agent-to-Agent) protocol](https://a2a-protocol.org).

Targets the **Work IQ Gateway** (`https://workiq.svc.cloud.microsoft/a2a/`) by default; override via the `Endpoint` key in `Configuration.plist`.

## Features

- **Microsoft 365 sign-in** via MSAL (silent + interactive)
- **A2A v1.0 protocol** over JSON-RPC transport
- **Streaming responses** via SSE with token-by-token display
- **Multi-turn conversations** with persistent context
- **Markdown rendering** in agent responses
- **Automatic token refresh** between turns

## Prerequisites

- **Xcode 26+** (Swift 6.0)
- iOS/iPadOS 26+ device or simulator
- A Microsoft 365 Copilot license on your test user
- An Entra app registration with the `WorkIQAgent.Ask` delegated permission and the iOS redirect URI (see [Setup](#azure-ad-app-registration) below)

> **Admin setup note**: For multi-language testing (this sample + the .NET samples sharing one app registration), use the unified [`../../ADMIN_SETUP.md`](../../ADMIN_SETUP.md) and `scripts/admin-setup.{sh,ps1}` at the repo root. The script in this folder (`setup-app-registration.{sh,ps1}`) is iOS-specific — it adds the iOS redirect URI (`msauth.app.blueglass.A2A-Chat://auth`) needed by MSAL on iOS, and generates `Configuration.plist`. Run it after the unified script (or instead of, if you only need the iOS app).

## Azure AD App Registration

The included scripts automate app registration. Pick whichever matches your environment:

### macOS / Linux

```bash
# Requires Azure CLI
# macOS:  brew install azure-cli
# Linux:  https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-linux
az login
./setup-app-registration.sh
```

### Windows

```powershell
# Requires Azure CLI
# Install: winget install Microsoft.AzureCLI
az login
.\setup-app-registration.ps1
```

Both scripts will:

1. Create an app registration named **A2A Chat**
2. Configure the iOS redirect URI (`msauth.app.blueglass.A2A-Chat://auth`)
3. Add the `WorkIQAgent.Ask` delegated permission
4. Grant admin consent
5. Generate `A2A Chat/Configuration.plist` with your App ID and the Work IQ Gateway endpoint

### Manual setup

If you already have an app registration:

1. Copy `A2A Chat/Configuration.example.plist` to `A2A Chat/Configuration.plist`
2. Replace `YOUR_APP_CLIENT_ID` with your Entra App (client) ID
3. (Optional) Override the `Endpoint` value if you're not using the default gateway URL

`Configuration.plist` is git-ignored so your client ID stays out of source control.

## Build & Run

1. Open `A2A Chat.xcodeproj` in Xcode.
2. Swift packages (MSAL, A2AClient) resolve automatically.
3. Select an iOS simulator or device and run.

## Configuration keys

| Key | Required | Default | Notes |
|-----|----------|---------|-------|
| `ClientId` | yes | — | Entra App (client) ID |
| `RedirectUri` | no | MSAL default | iOS redirect URI registered on the app |
| `TenantId` | no | `common` | Tenant ID or domain |
| `Scopes` | no | `[api://workiq.svc.cloud.microsoft/.default]` | OAuth scopes |
| `Endpoint` | no | `https://workiq.svc.cloud.microsoft/a2a/` | A2A endpoint |

## Architecture

```
A2A Chat/
├── A2A_ChatApp.swift              # App entry point, MSAL callback handling
├── Models/
│   └── ChatMessage.swift          # Chat message data model
├── Services/
│   ├── AuthService.swift          # MSAL auth + AppConfiguration loader
│   └── A2AService.swift           # A2A client (streaming & sync)
└── Views/
    ├── ContentView.swift          # Routes between Welcome and Chat
    ├── WelcomeView.swift          # Sign-in screen
    ├── ChatView.swift             # Chat interface
    └── MessageBubbleView.swift    # Message bubble with markdown
```

### Dependencies

| Package | Source | Purpose |
|---------|--------|---------|
| [MSAL](https://github.com/AzureAD/microsoft-authentication-library-for-objc) | AzureAD | Microsoft 365 authentication |
| [A2AClient](https://github.com/tolgaki/a2a-client-swift) (1.0.14+) | tolgaki | A2A v1.0 protocol client |

## License

[MIT](../../LICENCE)
