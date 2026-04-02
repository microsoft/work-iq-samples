use anyhow::{Context, Result};
use base64::engine::general_purpose::URL_SAFE_NO_PAD;
use base64::Engine;
use colored::Colorize;
use msal::broker::BrokerTokenRequest;
use msal::request::{AuthorizationCodeRequest, DeviceCodeRequest, SilentFlowRequest};
use msal::{Configuration, PublicClientApplication};
use std::sync::Arc;
use std::time::Duration;

// ── AuthManager ─────────────────────────────────────────────────────────

/// Manages the full token lifecycle using MSAL:
/// silent → broker → browser auth code → device code.
pub struct AuthManager {
    app: Arc<PublicClientApplication>,
    scopes: Vec<String>,
    last_token: Option<String>,
}

impl AuthManager {
    pub async fn new(
        client_id: &str,
        scopes: &[&str],
        authority: &str,
        _account_hint: Option<&str>,
    ) -> Result<Self> {
        let config = Configuration::builder(client_id)
            .authority(authority)
            .build();
        let app = PublicClientApplication::new(config)?;

        // Set up platform broker if available
        setup_broker(&app, authority).await;

        Ok(Self {
            app: Arc::new(app),
            scopes: scopes.iter().map(|s| s.to_string()).collect(),
            last_token: None,
        })
    }

    /// Get a valid access token. Tries (in order):
    /// 1. Silent acquisition (cached token / refresh token / broker silent)
    /// 2. Broker interactive (macOS Enterprise SSO / Windows WAM)
    /// 3. Browser auth code flow with PKCE
    /// 4. Device code flow (headless fallback)
    pub async fn get_token(&mut self, verbosity: u8) -> Result<String> {
        // 1. Try silent with any cached account
        if let Ok(accounts) = self.app.all_accounts().await {
            for account in accounts {
                if verbosity >= 2 {
                    eprintln!(
                        "  {}",
                        format!("Trying silent auth for {}...", account.username).dimmed()
                    );
                }
                match self
                    .app
                    .acquire_token_silent(SilentFlowRequest::new(
                        self.scopes.clone(),
                        account,
                    ))
                    .await
                {
                    Ok(result) => {
                        if verbosity >= 2 {
                            eprintln!("  {}", "Using cached token".dimmed());
                        }
                        let token = result.access_token.clone();
                        self.last_token = Some(token.clone());
                        return Ok(token);
                    }
                    Err(e) => {
                        if verbosity >= 1 {
                            eprintln!("  {} {}", "Silent auth failed:".yellow(), e);
                        }
                    }
                }
            }
        }

        // 2. Broker interactive
        if self.app.is_broker_available().await {
            if verbosity >= 1 {
                eprintln!("  {}", "Trying SSO broker...".dimmed());
            }
            match self.try_broker_interactive().await {
                Ok(token) => return Ok(token),
                Err(e) => {
                    if verbosity >= 1 {
                        eprintln!("  {} {}", "Broker auth failed:".yellow(), e);
                    }
                }
            }
        }

        // 3. Browser auth code flow
        if verbosity >= 1 {
            eprintln!("  {}", "Opening browser for sign-in...".dimmed());
        }
        match self.try_browser_auth(verbosity).await {
            Ok(token) => return Ok(token),
            Err(e) => {
                if verbosity >= 1 {
                    eprintln!("  {} {}", "Browser auth failed:".yellow(), e);
                }
            }
        }

        // 4. Device code flow (last resort)
        if verbosity >= 1 {
            eprintln!("  {}", "Falling back to device code login...".dimmed());
        }
        let result = self
            .app
            .acquire_token_by_device_code(
                DeviceCodeRequest::new(self.scopes.clone()),
                |info| {
                    println!("\n  {}", info.message.yellow().bold());
                    println!(
                        "  Code: {}  URL: {}\n",
                        info.user_code.green().bold(),
                        info.verification_uri.underline()
                    );
                },
            )
            .await?;

        let token = result.access_token.clone();
        self.last_token = Some(token.clone());
        Ok(token)
    }

    /// Ensure the token is fresh before a request. Refreshes silently if needed.
    /// Falls back to the last known token if silent refresh fails.
    pub async fn ensure_fresh(&mut self, verbosity: u8) -> Result<String> {
        if let Ok(accounts) = self.app.all_accounts().await {
            if let Some(account) = accounts.into_iter().next() {
                match self
                    .app
                    .acquire_token_silent(SilentFlowRequest::new(
                        self.scopes.clone(),
                        account,
                    ))
                    .await
                {
                    Ok(result) => {
                        let token = result.access_token.clone();
                        self.last_token = Some(token.clone());
                        return Ok(token);
                    }
                    Err(e) => {
                        if verbosity >= 1 {
                            eprintln!("  {} {}", "Silent refresh failed:".yellow(), e);
                        }
                    }
                }
            }
        }

        self.last_token
            .clone()
            .ok_or_else(|| anyhow::anyhow!("No token available. Run `login` to authenticate."))
    }

    pub async fn cached_account(&self) -> Option<String> {
        self.app
            .all_accounts()
            .await
            .ok()
            .and_then(|a| a.into_iter().next())
            .map(|a| a.username)
    }

    pub async fn has_accounts(&self) -> bool {
        self.app
            .all_accounts()
            .await
            .map(|a| !a.is_empty())
            .unwrap_or(false)
    }

    pub async fn sign_out_all(&self) -> Result<()> {
        if let Ok(accounts) = self.app.all_accounts().await {
            for account in &accounts {
                self.app.sign_out(account).await.ok();
            }
        }
        Ok(())
    }

    // ── Broker ──────────────────────────────────────────────────────────

    async fn try_broker_interactive(&mut self) -> Result<String> {
        let request = BrokerTokenRequest {
            scopes: self.scopes.clone(),
            account: None,
            claims: None,
            correlation_id: None,
            window_handle: None,
            authentication_scheme: Default::default(),
            pop_params: None,
        };

        // On macOS, the broker dispatches to the main thread via GCD.
        // In a tokio runtime the main run loop may not be active, so we
        // use a timeout to avoid hanging indefinitely.
        let result = tokio::time::timeout(
            Duration::from_secs(15),
            self.app.acquire_token_interactive(request),
        )
        .await
        .context("Broker timed out")??;

        let token = result.access_token.clone();
        self.last_token = Some(token.clone());
        Ok(token)
    }

    // ── Browser Auth Code ───────────────────────────────────────────────

    async fn try_browser_auth(&mut self, verbosity: u8) -> Result<String> {
        use tokio::io::{AsyncReadExt, AsyncWriteExt};

        // Bind a random port on localhost
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await?;
        let port = listener.local_addr()?.port();
        let redirect_uri = format!("http://localhost:{port}");

        // Generate authorization URL with PKCE
        let (auth_url, pkce) =
            self.app
                .authorization_url(&self.scopes, &redirect_uri, None)?;

        if verbosity >= 2 {
            eprintln!("  Redirect: {redirect_uri}");
        }

        // Open the browser
        open::that(&auth_url).context("Failed to open browser")?;
        eprintln!(
            "  {}",
            "Waiting for sign-in in your browser...".dimmed()
        );

        // Wait for the redirect (2 minute timeout)
        let (mut stream, _) = tokio::time::timeout(Duration::from_secs(120), listener.accept())
            .await
            .context("Timed out waiting for browser sign-in")??;

        let mut buf = vec![0u8; 8192];
        let n = stream.read(&mut buf).await?;
        let request_str = String::from_utf8_lossy(&buf[..n]);

        // Extract authorization code from the GET request
        let code = extract_code_from_request(&request_str)?;

        // Send success page back to the browser
        let html = "<html><body style=\"font-family:system-ui;text-align:center;padding:60px\">\
                     <h2>Sign-in complete</h2>\
                     <p>You can close this tab and return to the terminal.</p>\
                     </body></html>";
        let response = format!(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{html}",
            html.len()
        );
        stream.write_all(response.as_bytes()).await.ok();
        stream.shutdown().await.ok();

        // Exchange authorization code for token
        let mut token_request =
            AuthorizationCodeRequest::new(code, self.scopes.clone(), redirect_uri);
        token_request.code_verifier = Some(pkce.verifier);

        let result = self.app.acquire_token_by_code(token_request).await?;

        let token = result.access_token.clone();
        self.last_token = Some(token.clone());
        Ok(token)
    }
}

// ── Broker Setup ────────────────────────────────────────────────────────

async fn setup_broker(app: &PublicClientApplication, authority: &str) {
    #[cfg(target_os = "macos")]
    {
        if let Ok(broker) = msal::broker::macos::MacOsBroker::new_for_cli(authority) {
            app.set_broker(Box::new(broker)).await;
        }
    }

    #[cfg(target_os = "windows")]
    {
        if let Ok(broker) = msal::broker::wam::WamBroker::with_authority(authority).await {
            app.set_broker(Box::new(broker)).await;
        }
    }

    // Suppress unused variable warning on platforms without broker support
    let _ = (app, authority);
}

// ── Browser Redirect Parsing ────────────────────────────────────────────

fn extract_code_from_request(request: &str) -> Result<String> {
    // Parse: GET /?code=xxx&state=yyy HTTP/1.1
    let first_line = request.lines().next().context("Empty HTTP request")?;
    let path = first_line
        .split_whitespace()
        .nth(1)
        .context("Malformed HTTP request")?;

    let query = path.split('?').nth(1).unwrap_or("");

    // Check for error first
    let mut error = None;
    let mut error_desc = None;
    let mut code = None;

    for param in query.split('&') {
        if let Some(val) = param.strip_prefix("code=") {
            code = Some(val.to_string());
        } else if let Some(val) = param.strip_prefix("error=") {
            error = Some(val.to_string());
        } else if let Some(val) = param.strip_prefix("error_description=") {
            error_desc = Some(val.replace('+', " "));
        }
    }

    if let Some(err) = error {
        anyhow::bail!(
            "Authorization failed: {} — {}",
            err,
            error_desc.unwrap_or_default()
        );
    }

    code.context("No authorization code in redirect")
}

// ── JWT Display ─────────────────────────────────────────────────────────

/// Decode and display key JWT claims (matching .NET CLI output format).
pub fn decode_token(token: &str) {
    let parts: Vec<&str> = token.split('.').collect();
    if parts.len() != 3 {
        eprintln!("{}", "  Token is not a valid JWT (expected 3 parts)".red());
        return;
    }

    let payload = match decode_jwt_part(parts[1]) {
        Ok(p) => p,
        Err(e) => {
            eprintln!("  {} {}", "decode failed:".red(), e);
            return;
        }
    };

    for claim in &[
        "aud",
        "appid",
        "app_displayname",
        "tid",
        "upn",
        "name",
        "scp",
    ] {
        if let Some(val) = payload.get(claim).and_then(|v| v.as_str()) {
            if !val.is_empty() {
                println!("  {:<16} {}", claim, val);
            }
        }
    }

    if let Some(exp) = payload.get("exp").and_then(|v| v.as_i64()) {
        let now = chrono::Utc::now().timestamp();
        let remaining = exp - now;
        if remaining > 0 {
            let time_str = format!("{}m", remaining / 60);
            if remaining < 300 {
                println!("  {:<16} {}", "expires", time_str.red());
            } else {
                println!("  {:<16} {}", "expires", time_str);
            }
        } else {
            println!("  {:<16} {}", "expires", "EXPIRED".red().bold());
        }
    }
}

fn decode_jwt_part(part: &str) -> Result<serde_json::Value> {
    let decoded = URL_SAFE_NO_PAD
        .decode(part.trim_end_matches('='))
        .context("base64 decode")?;
    serde_json::from_slice(&decoded).context("JSON parse")
}
