use a2a_rs_client::{A2aClient, ClientConfig};
use a2a_rs_core::{Message, SendMessageConfiguration, SendMessageResult, StreamingMessageResult};
use anyhow::{Context, Result};
use futures_util::Stream;
use reqwest::header::{HeaderMap, HeaderName, HeaderValue};
use std::pin::Pin;
use std::time::Duration;

/// Thin wrapper around `A2aClient` configured for the Work IQ Gateway (A2A v1.0).
///
/// Bypasses agent-card discovery via `endpoint_url` since the gateway path is
/// already the A2A endpoint. The token is sent per-request, so `update_token`
/// is cheap. Uses the SDK's default protocol version (V1.0) — PascalCase
/// methods + `A2A-Version: 1.0` header.
pub struct WorkIQClient {
    client: A2aClient,
    token: String,
}

impl WorkIQClient {
    pub fn new(endpoint: &str, http: reqwest::Client, token: &str) -> Result<Self> {
        let client = A2aClient::new(ClientConfig {
            server_url: endpoint.to_string(),
            endpoint_url: Some(endpoint.to_string()),
            http_client: Some(http),
            ..Default::default()
        })?;
        Ok(Self {
            client,
            token: token.to_string(),
        })
    }

    pub fn update_token(&mut self, token: &str) {
        self.token = token.to_string();
    }

    pub async fn send_message(
        &self,
        message: Message,
        configuration: Option<SendMessageConfiguration>,
    ) -> Result<SendMessageResult> {
        self.client
            .send_message(message, Some(&self.token), configuration)
            .await
    }

    pub async fn send_message_streaming(
        &self,
        message: Message,
        configuration: Option<SendMessageConfiguration>,
    ) -> Result<Pin<Box<dyn Stream<Item = Result<StreamingMessageResult>> + Send>>> {
        self.client
            .send_message_streaming(message, Some(&self.token), configuration)
            .await
    }
}

/// Build a shared reqwest client with default headers and a 5-minute timeout.
/// Used both for A2A POSTs and gateway-discovery GETs.
pub fn build_http_client(extra_headers: &[String]) -> Result<reqwest::Client> {
    let mut headers = HeaderMap::new();
    for h in extra_headers {
        let (k, v) = h
            .split_once(':')
            .with_context(|| format!("Invalid header (expected 'Key: Value'): {h}"))?;
        let name: HeaderName = k.trim().parse().context("invalid header name")?;
        let value = HeaderValue::from_str(v.trim()).context("invalid header value")?;
        headers.insert(name, value);
    }
    reqwest::Client::builder()
        .default_headers(headers)
        .timeout(Duration::from_secs(300))
        .build()
        .context("building reqwest client")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn build_http_client_accepts_valid_headers() {
        let client = build_http_client(&["X-Trace-Id: abc".into(), "X-Other:val".into()]);
        assert!(client.is_ok());
    }

    #[test]
    fn build_http_client_rejects_missing_colon() {
        let err = build_http_client(&["bad header".into()]).unwrap_err();
        assert!(err.to_string().contains("Key: Value"), "got: {err}");
    }

    #[test]
    fn build_http_client_rejects_invalid_name() {
        let err = build_http_client(&["bad name: val".into()]).unwrap_err();
        assert!(err.to_string().contains("invalid header name"), "got: {err}");
    }
}
