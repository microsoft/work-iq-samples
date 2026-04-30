//! Gateway-extension endpoints: agent registry (`/.agents`) and A2A agent
//! card resolution (`{endpoint}/{agent-id}/.well-known/agent-card.json`).
//!
//! `/.agents` is a Work IQ / Sydney extension (not part of the A2A spec) that
//! returns an array of `{agentId, name, provider}` entries. `agent-card.json`
//! is the standard A2A discovery document.

use anyhow::{Context, Result};
use colored::Colorize;
use reqwest::header::ACCEPT;
use serde::Deserialize;

#[derive(Debug, Deserialize)]
pub struct AgentInfo {
    #[serde(rename = "agentId", default)]
    pub agent_id: String,
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub provider: String,
}

#[derive(Debug, Deserialize)]
pub struct AgentCard {
    pub url: String,
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub capabilities: Capabilities,
}

#[derive(Debug, Default, Deserialize)]
pub struct Capabilities {
    #[serde(default)]
    pub streaming: bool,
}

pub async fn list_agents(
    http: &reqwest::Client,
    endpoint: &str,
    token: &str,
) -> Result<Vec<AgentInfo>> {
    let url = format!("{}/.agents", endpoint.trim_end_matches('/'));
    let response = http
        .get(&url)
        .bearer_auth(token)
        .header(ACCEPT, "application/json")
        .send()
        .await
        .with_context(|| format!("GET {url}"))?;

    let status = response.status();
    if !status.is_success() {
        let body = response.text().await.unwrap_or_default();
        let trimmed = body.trim();
        if trimmed.is_empty() {
            anyhow::bail!("{status} from {url}");
        }
        anyhow::bail!("{status} from {url}: {trimmed}");
    }

    response
        .json()
        .await
        .with_context(|| format!("parsing JSON from {url}"))
}

pub async fn fetch_agent_card(
    http: &reqwest::Client,
    endpoint: &str,
    agent_id: &str,
    token: &str,
) -> Result<AgentCard> {
    let url = format!(
        "{}/{}/.well-known/agent-card.json",
        endpoint.trim_end_matches('/'),
        agent_id
    );
    let response = http
        .get(&url)
        .bearer_auth(token)
        .header(ACCEPT, "application/json")
        .send()
        .await
        .with_context(|| format!("GET {url}"))?;

    let status = response.status();
    if !status.is_success() {
        anyhow::bail!("failed to fetch agent card: {status} from {url}");
    }

    response
        .json()
        .await
        .with_context(|| format!("parsing JSON from {url}"))
}

pub fn print_agents(endpoint: &str, agents: &[AgentInfo]) {
    println!();
    println!("Agents at {endpoint}:");
    println!();

    if agents.is_empty() {
        println!("  (none)");
        return;
    }

    let id_width = agents.iter().map(|a| a.agent_id.len()).max().unwrap_or(0).max(8);
    let name_width = agents.iter().map(|a| a.name.len()).max().unwrap_or(0).max(4);

    println!(
        "  {}  {}  {}",
        format!("{:<id_width$}", "AGENT ID").dimmed(),
        format!("{:<name_width$}", "NAME").dimmed(),
        "PROVIDER".dimmed(),
    );
    for a in agents {
        println!(
            "  {:<id_width$}  {:<name_width$}  {}",
            a.agent_id, a.name, a.provider
        );
    }

    println!();
    let n = agents.len();
    println!("{n} agent{}.", if n == 1 { "" } else { "s" });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn agent_info_deserializes_with_partial_fields() {
        let json = r#"{"agentId":"x","name":"Bot"}"#;
        let info: AgentInfo = serde_json::from_str(json).unwrap();
        assert_eq!(info.agent_id, "x");
        assert_eq!(info.name, "Bot");
        assert_eq!(info.provider, "");
    }

    #[test]
    fn agent_info_deserializes_with_unknown_fields() {
        let json = r#"{"agentId":"x","name":"Bot","provider":"M","extra":42}"#;
        let info: AgentInfo = serde_json::from_str(json).unwrap();
        assert_eq!(info.agent_id, "x");
        assert_eq!(info.provider, "M");
    }

    #[test]
    fn agent_info_missing_agent_id_defaults_to_empty() {
        let json = r#"{"name":"Bot"}"#;
        let info: AgentInfo = serde_json::from_str(json).unwrap();
        assert_eq!(info.agent_id, "");
        assert_eq!(info.name, "Bot");
    }

    #[test]
    fn agent_card_parses_streaming_capability() {
        let json = r#"{
            "name":"Researcher",
            "url":"https://example.com/agents/researcher/",
            "capabilities":{"streaming":true,"pushNotifications":false}
        }"#;
        let card: AgentCard = serde_json::from_str(json).unwrap();
        assert_eq!(card.url, "https://example.com/agents/researcher/");
        assert_eq!(card.name, "Researcher");
        assert!(card.capabilities.streaming);
    }

    #[test]
    fn agent_card_defaults_capabilities_when_missing() {
        let json = r#"{"url":"https://example.com/x"}"#;
        let card: AgentCard = serde_json::from_str(json).unwrap();
        assert_eq!(card.url, "https://example.com/x");
        assert!(!card.capabilities.streaming);
    }

    #[test]
    fn agent_card_requires_url() {
        let json = r#"{"name":"x"}"#;
        let err = serde_json::from_str::<AgentCard>(json).unwrap_err();
        assert!(err.to_string().contains("url"), "got: {err}");
    }
}
