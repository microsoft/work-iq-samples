# Work IQ A2A CLI (Rust)

A Rust command-line tool for interactive [A2A v1.0 (Agent-to-Agent)](https://a2a-protocol.org/latest/specification/) sessions against the **Work IQ Gateway** (`https://workiq.svc.cloud.microsoft/a2a/`).

## Features

- **Interactive REPL** with multi-turn conversation support
- **MSAL auth** with silent → broker → browser PKCE → device code fallback
- **Configurable verbosity** (`-v 0|1|2`) for debugging wire-level details

## Prerequisites

- [Rust](https://www.rust-lang.org/tools/install) 1.70+
- A Microsoft 365 Copilot license on your test user
- An Entra app registration with the `WorkIQAgent.Ask` delegated permission. See [Setup](#azure-ad-app-registration) below.

> **Admin setup note**: For multi-language testing (this sample + the .NET samples sharing one app registration), use the unified [`../../ADMIN_SETUP.md`](../../ADMIN_SETUP.md) and `scripts/admin-setup.{sh,ps1}` at the repo root. The script in this folder (`setup-app-registration.{sh,ps1}`) is Rust-specific (no WAM redirect URI).

## Quick start

```bash
# Build
cargo build --release

# Sign in and start chatting (defaults to the Work IQ Gateway)
cargo run -- --appid <APP_ID>
```

First run uses MSAL's interactive flow (broker on macOS/Windows, browser PKCE elsewhere, device code as a final fallback). Tokens are cached and refreshed silently between turns.

## Usage

```
workiq-a2a [OPTIONS] [COMMAND]
```

### Commands

| Command  | Description                              |
|----------|------------------------------------------|
| `login`  | Sign in interactively and cache the token |
| `logout` | Clear cached tokens                       |
| `status` | Show current auth status and token info   |

If no command is given, the CLI enters the interactive REPL.

### Options

| Flag | Description | Default |
|------|-------------|---------|
| `--appid, -a <ID>` | Entra application (client) ID. Also `WORKIQ_APP_ID` env var. | — |
| `--token <JWT>` | Pre-obtained bearer token (skips MSAL) | — |
| `--account <EMAIL>` | M365 account hint (e.g. `user@contoso.com`) | — |
| `--header, -H <K: V>` | Custom HTTP header (repeatable) | — |
| `-v, --verbosity <N>` | 0=quiet, 1=normal, 2=wire | `1` |
| `--show-token` | Print the raw JWT after sign-in | — |

### Examples

```bash
# Default — talk to the Work IQ Gateway
cargo run -- --appid <APP_ID>

# Pre-obtained JWT (any platform — useful in CI/automation)
cargo run -- --token "eyJ0eXAi..."

# Verbose wire-level output
cargo run -- --appid <APP_ID> -v 2 --show-token

# Check auth status
cargo run -- --appid <APP_ID> status
```

### REPL inputs

- Type a message and press Enter.
- `quit` or `exit` to end the session.
- `Ctrl+C` to interrupt.

## Azure AD app registration

```bash
# macOS / Linux (requires Azure CLI)
az login
./setup-app-registration.sh
```

```powershell
# Windows
az login
.\setup-app-registration.ps1
```

The script creates a single-tenant public client, adds the `WorkIQAgent.Ask` delegated permission, grants admin consent, and prints the resulting App ID. Pass it with `--appid` or set `WORKIQ_APP_ID`.

## Architecture

```
src/
├── main.rs       # CLI entry, REPL loop, output formatting
├── config.rs     # clap arg parsing + Work IQ Gateway constants
├── auth.rs       # MSAL: silent → broker → browser PKCE → device code; JWT decode
└── a2a.rs        # A2A client wrapper (a2a-rs-client)
```

### Auth flow

1. **Silent** — use any cached token / refresh token / broker silent
2. **Broker** — macOS Enterprise SSO / Windows WAM, when available
3. **Browser PKCE** — random localhost port, opens default browser
4. **Device code** — final fallback for headless contexts

Tokens are cached at `~/.workiq/token_cache.json` (Unix permissions `0600`; on Windows the file relies on user-level NTFS permissions).

## License

[MIT](../../LICENCE)
