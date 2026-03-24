use anyhow::{Context, Result};
use base64::engine::general_purpose::URL_SAFE_NO_PAD;
use base64::Engine;
use colored::Colorize;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;

// ── Token Cache ─────────────────────────────────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TokenCache {
    pub client_id: String,
    pub access_token: String,
    pub refresh_token: Option<String>,
    /// Unix timestamp when the access token expires.
    pub expires_at: i64,
    pub account: Option<String>,
}

fn cache_dir() -> PathBuf {
    dirs::home_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join(".workiq")
}

fn cache_path() -> PathBuf {
    cache_dir().join("token_cache.json")
}

impl TokenCache {
    pub fn load() -> Option<Self> {
        let data = std::fs::read_to_string(cache_path()).ok()?;
        serde_json::from_str(&data).ok()
    }

    pub fn save(&self) -> Result<()> {
        let dir = cache_dir();
        std::fs::create_dir_all(&dir)
            .with_context(|| format!("Failed to create {}", dir.display()))?;
        let path = cache_path();
        let data = serde_json::to_string_pretty(self)?;
        std::fs::write(&path, data)
            .with_context(|| format!("Failed to write {}", path.display()))?;
        // Restrict permissions on unix
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o600)).ok();
        }
        Ok(())
    }

    pub fn clear() -> Result<()> {
        let path = cache_path();
        if path.exists() {
            std::fs::remove_file(&path)
                .with_context(|| format!("Failed to remove {}", path.display()))?;
        }
        Ok(())
    }

    pub fn is_expired(&self) -> bool {
        let now = chrono::Utc::now().timestamp();
        now >= self.expires_at
    }

    /// Returns true if the token will expire within the given number of seconds.
    pub fn expires_within(&self, secs: i64) -> bool {
        let now = chrono::Utc::now().timestamp();
        (self.expires_at - now) < secs
    }
}

// ── AuthManager ─────────────────────────────────────────────────────────

/// Manages the full token lifecycle: cache, silent refresh, device code fallback.
pub struct AuthManager {
    client_id: String,
    scopes: Vec<String>,
    authority: String,
    account_hint: Option<String>,
    http: reqwest::Client,
    cache: Option<TokenCache>,
}

impl AuthManager {
    pub fn new(
        client_id: &str,
        scopes: &[&str],
        authority: &str,
        account_hint: Option<&str>,
    ) -> Self {
        let cache = TokenCache::load().filter(|c| c.client_id == client_id);
        Self {
            client_id: client_id.to_string(),
            scopes: scopes.iter().map(|s| s.to_string()).collect(),
            authority: authority.to_string(),
            account_hint: account_hint.map(|s| s.to_string()),
            http: reqwest::Client::new(),
            cache,
        }
    }

    /// Get a valid access token. Tries (in order):
    /// 1. Cached token if still valid
    /// 2. Silent refresh via refresh_token
    /// 3. Device code flow (interactive)
    pub async fn get_token(&mut self, verbosity: u8) -> Result<String> {
        // 1. Cached and still fresh (>60s remaining)
        if let Some(ref cache) = self.cache {
            if !cache.expires_within(60) {
                if verbosity >= 2 {
                    eprintln!("  {}", "Using cached access token".dimmed());
                }
                return Ok(cache.access_token.clone());
            }
        }

        // 2. Try silent refresh
        if let Some(rt) = self.cache.as_ref().and_then(|c| c.refresh_token.clone()) {
            if verbosity >= 1 {
                eprintln!("  {}", "Refreshing token...".dimmed());
            }
            match self.refresh_token(&rt).await {
                Ok(new_cache) => {
                    let token = new_cache.access_token.clone();
                    self.cache = Some(new_cache);
                    return Ok(token);
                }
                Err(e) => {
                    if verbosity >= 1 {
                        eprintln!(
                            "  {} {}",
                            "Refresh failed:".yellow(),
                            e
                        );
                    }
                    // Fall through to device code
                }
            }
        }

        // 3. Device code flow
        if verbosity >= 1 {
            eprintln!("  {}", "Starting device code login...".dimmed());
        }
        let new_cache = self.device_code_flow().await?;
        let token = new_cache.access_token.clone();
        self.cache = Some(new_cache);
        Ok(token)
    }

    /// Ensure the token is fresh before a request. Returns the current access token,
    /// refreshing silently if needed. Returns None only if refresh fails and there's
    /// no way to get a token non-interactively.
    pub async fn ensure_fresh(&mut self, verbosity: u8) -> Result<String> {
        if let Some(ref cache) = self.cache {
            if !cache.expires_within(300) {
                return Ok(cache.access_token.clone());
            }
        }
        // Token is stale or near expiry — try silent refresh
        if let Some(rt) = self.cache.as_ref().and_then(|c| c.refresh_token.clone()) {
            if verbosity >= 2 {
                eprintln!("  {}", "Silently refreshing token...".dimmed());
            }
            match self.refresh_token(&rt).await {
                Ok(new_cache) => {
                    let token = new_cache.access_token.clone();
                    self.cache = Some(new_cache);
                    return Ok(token);
                }
                Err(e) => {
                    if verbosity >= 1 {
                        eprintln!("  {} {}", "Silent refresh failed:".yellow(), e);
                    }
                }
            }
        }
        // Return whatever we have; caller will get a 401 if it's truly dead
        if let Some(ref cache) = self.cache {
            Ok(cache.access_token.clone())
        } else {
            anyhow::bail!("No token available. Run `login` to authenticate.");
        }
    }

    pub fn cached_account(&self) -> Option<&str> {
        self.cache.as_ref()?.account.as_deref()
    }

    // ── Silent Refresh ──────────────────────────────────────────────

    async fn refresh_token(&mut self, refresh_token: &str) -> Result<TokenCache> {
        let token_url = format!("{}/oauth2/v2.0/token", self.authority);
        let scope = self.scopes.join(" ");

        let resp = self
            .http
            .post(&token_url)
            .form(&[
                ("client_id", self.client_id.as_str()),
                ("grant_type", "refresh_token"),
                ("refresh_token", refresh_token),
                ("scope", &scope),
            ])
            .send()
            .await?;

        let body = resp.bytes().await?;
        let token_resp: FullTokenResponse = serde_json::from_slice(&body)
            .with_context(|| {
                let err: Option<TokenErrorResponse> = serde_json::from_slice(&body).ok();
                format!(
                    "Token refresh failed: {}",
                    err.map(|e| format!("{}: {}", e.error, e.error_description.unwrap_or_default()))
                        .unwrap_or_else(|| "unknown error".into())
                )
            })?;

        let now = chrono::Utc::now().timestamp();
        let cache = TokenCache {
            client_id: self.client_id.clone(),
            access_token: token_resp.access_token,
            refresh_token: token_resp.refresh_token.or_else(|| {
                // Keep the old refresh token if server didn't rotate
                Some(refresh_token.to_string())
            }),
            expires_at: now + token_resp.expires_in as i64,
            account: self
                .account_hint
                .clone()
                .or_else(|| self.cache.as_ref().and_then(|c| c.account.clone())),
        };
        cache.save()?;
        Ok(cache)
    }

    // ── Device Code Flow ────────────────────────────────────────────

    async fn device_code_flow(&self) -> Result<TokenCache> {
        let scope = self.scopes.join(" ");

        // Add offline_access to get a refresh token
        let scope_with_offline = if scope.contains("offline_access") {
            scope.clone()
        } else {
            format!("{scope} offline_access")
        };

        let dc_url = format!("{}/oauth2/v2.0/devicecode", self.authority);
        let resp = self
            .http
            .post(&dc_url)
            .form(&[
                ("client_id", self.client_id.as_str()),
                ("scope", scope_with_offline.as_str()),
            ])
            .send()
            .await?;

        let body = resp.bytes().await?;

        // Check for error response first
        if let Ok(err) = serde_json::from_slice::<TokenErrorResponse>(&body) {
            if err.error_description.is_some() || err.error != "authorization_pending" {
                anyhow::bail!(
                    "Device code request failed: {} — {}",
                    err.error,
                    err.error_description.unwrap_or_default()
                );
            }
        }

        let dc_resp: DeviceCodeResponse = serde_json::from_slice(&body)
            .with_context(|| {
                format!(
                    "Failed to parse device code response: {}",
                    String::from_utf8_lossy(&body)
                )
            })?;

        println!("\n  {}", dc_resp.message.yellow().bold());
        println!(
            "  Code: {}  URL: {}\n",
            dc_resp.user_code.green().bold(),
            dc_resp.verification_uri.underline()
        );

        // Poll for token
        let token_url = format!("{}/oauth2/v2.0/token", self.authority);
        let interval = std::time::Duration::from_secs(dc_resp.interval.max(5));
        let deadline =
            tokio::time::Instant::now() + std::time::Duration::from_secs(dc_resp.expires_in);

        loop {
            tokio::time::sleep(interval).await;

            if tokio::time::Instant::now() > deadline {
                anyhow::bail!("Device code flow timed out — please try again");
            }

            let resp = self
                .http
                .post(&token_url)
                .form(&[
                    ("client_id", self.client_id.as_str()),
                    ("device_code", dc_resp.device_code.as_str()),
                    (
                        "grant_type",
                        "urn:ietf:params:oauth:grant-type:device_code",
                    ),
                ])
                .send()
                .await?;

            let body = resp.bytes().await?;

            if let Ok(token_resp) = serde_json::from_slice::<FullTokenResponse>(&body) {
                let now = chrono::Utc::now().timestamp();

                // Try to extract account from the id_token or access_token
                let account = extract_account(&token_resp.access_token)
                    .or_else(|| self.account_hint.clone());

                let cache = TokenCache {
                    client_id: self.client_id.clone(),
                    access_token: token_resp.access_token,
                    refresh_token: token_resp.refresh_token,
                    expires_at: now + token_resp.expires_in as i64,
                    account,
                };
                cache.save()?;
                return Ok(cache);
            }

            if let Ok(err_resp) = serde_json::from_slice::<TokenErrorResponse>(&body) {
                match err_resp.error.as_str() {
                    "authorization_pending" => continue,
                    "slow_down" => {
                        tokio::time::sleep(std::time::Duration::from_secs(5)).await;
                        continue;
                    }
                    _ => {
                        anyhow::bail!(
                            "Login failed: {} — {}",
                            err_resp.error,
                            err_resp.error_description.unwrap_or_default()
                        );
                    }
                }
            }

            anyhow::bail!("Unexpected response during login");
        }
    }
}

// ── JWT Helpers ──────────────────────────────────────────────────────────

/// Decode and display JWT token claims (header + payload) without verification.
pub fn decode_token(token: &str) {
    let parts: Vec<&str> = token.split('.').collect();
    if parts.len() != 3 {
        eprintln!("{}", "  Token is not a valid JWT (expected 3 parts)".red());
        return;
    }

    if let Ok(header) = decode_jwt_part(parts[0]) {
        println!("  {}", "Header:".dimmed());
        print_json_pretty(&header, "    ");
    }

    if let Ok(payload) = decode_jwt_part(parts[1]) {
        println!("  {}", "Payload:".dimmed());
        print_json_pretty(&payload, "    ");

        if let Some(exp) = payload.get("exp").and_then(|v| v.as_i64()) {
            let now = chrono::Utc::now().timestamp();
            let remaining = exp - now;
            if remaining > 0 {
                println!(
                    "  {} {}m {}s",
                    "Expires in:".dimmed(),
                    remaining / 60,
                    remaining % 60
                );
            } else {
                println!("  {}", "TOKEN EXPIRED".red().bold());
            }
        }
    }
}

/// Extract the UPN or preferred_username from a JWT access token.
fn extract_account(token: &str) -> Option<String> {
    let parts: Vec<&str> = token.split('.').collect();
    if parts.len() != 3 {
        return None;
    }
    let payload = decode_jwt_part(parts[1]).ok()?;
    payload
        .get("upn")
        .or_else(|| payload.get("preferred_username"))
        .or_else(|| payload.get("unique_name"))
        .and_then(|v| v.as_str())
        .map(|s| s.to_string())
}

fn decode_jwt_part(part: &str) -> Result<serde_json::Value> {
    let decoded = URL_SAFE_NO_PAD
        .decode(part.trim_end_matches('='))
        .context("base64 decode")?;
    serde_json::from_slice(&decoded).context("JSON parse")
}

fn print_json_pretty(val: &serde_json::Value, indent: &str) {
    if let Some(obj) = val.as_object() {
        for (k, v) in obj {
            let v_str = match v {
                serde_json::Value::String(s) => {
                    if s.len() > 80 {
                        let truncated: String = s.chars().take(80).collect();
                        format!("{truncated}...")
                    } else {
                        s.clone()
                    }
                }
                other => other.to_string(),
            };
            println!("{}{}: {}", indent, k.cyan(), v_str);
        }
    }
}

// ── OAuth2 Response Types ───────────────────────────────────────────────

#[derive(Debug, Deserialize)]
struct DeviceCodeResponse {
    device_code: String,
    user_code: String,
    verification_uri: String,
    expires_in: u64,
    interval: u64,
    message: String,
}

#[derive(Debug, Deserialize)]
struct FullTokenResponse {
    access_token: String,
    #[allow(dead_code)]
    token_type: Option<String>,
    expires_in: u64,
    refresh_token: Option<String>,
}

#[derive(Debug, Deserialize)]
struct TokenErrorResponse {
    error: String,
    error_description: Option<String>,
}
