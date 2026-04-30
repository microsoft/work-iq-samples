mod a2a;
mod auth;
mod config;

use a2a::{build_http_client, WorkIQClient};
use a2a_rs_core::{Message, Part, Role, SendMessageConfiguration, SendMessageResult};
use auth::{decode_token, AuthManager};
use clap::Parser;
use colored::Colorize;
use config::{Cli, Command, WORKIQ_AUTHORITY, WORKIQ_ENDPOINT, WORKIQ_SCOPES};

use std::io::{self, Write};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Instant;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();
    let verbosity = cli.verbosity;
    let app_id = cli.appid.clone().unwrap_or_default();

    // ── Subcommands (login / logout / status) ────────────────────────
    if let Some(cmd) = &cli.command {
        require_app_id(&app_id)?;
        return run_subcommand(cmd, &app_id, &cli).await;
    }

    // ── Resolve token for REPL ───────────────────────────────────────
    let (mut token, mut auth_mgr) = if let Some(raw) = cli.token.clone() {
        (raw, None)
    } else {
        require_app_id(&app_id)?;
        let mut mgr =
            AuthManager::new(&app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, cli.account.as_deref()).await?;
        let token = mgr.get_token(verbosity).await?;
        (token, Some(mgr))
    };

    if verbosity >= 1 {
        log_header("TOKEN");
        decode_token(&token);
        if cli.show_token {
            println!("\n  {token}\n");
        }
    }

    // ── HTTP + A2A client ────────────────────────────────────────────
    let http = build_http_client(&cli.headers)?;
    let mut client = WorkIQClient::new(WORKIQ_ENDPOINT, http, &token)?;
    let mut context_id: Option<String> = None;

    if verbosity >= 1 {
        log_header(&format!("READY — Work IQ Gateway — {WORKIQ_ENDPOINT}"));
        if let Some(mgr) = auth_mgr.as_ref() {
            if let Some(acct) = mgr.cached_account().await {
                println!("  Signed in as {}", acct.cyan());
            }
        }
        println!("Type a message. 'quit' to exit.\n");
    }

    loop {
        if verbosity >= 1 {
            print!("{}", "You > ".cyan());
            io::stdout().flush()?;
        }

        let mut input = String::new();
        if io::stdin().read_line(&mut input)? == 0 {
            break;
        }
        let input = input.trim();
        if input.is_empty() {
            continue;
        }
        if input.eq_ignore_ascii_case("quit") || input.eq_ignore_ascii_case("exit") {
            break;
        }

        // Silent token refresh between turns
        if let Some(mgr) = auth_mgr.as_mut() {
            if let Ok(fresh) = mgr.ensure_fresh(verbosity).await {
                if fresh != token {
                    token = fresh;
                    client.update_token(&token);
                }
            }
        }

        if verbosity >= 1 {
            print!("{}", "Agent > ".green());
            io::stdout().flush()?;
        }

        let spinner = Spinner::start();
        let message = build_message(Role::User, input, context_id.clone());
        let send_config = Some(SendMessageConfiguration {
            accepted_output_modes: Some(vec!["text/plain".to_string()]),
            task_push_notification_config: None,
            history_length: None,
            return_immediately: None,
        });

        let started = Instant::now();
        let result = handle_sync(&client, message, send_config, &mut context_id, &spinner).await;

        spinner.stop();
        match result {
            Ok(metadata) => {
                if verbosity >= 1 {
                    println!("  {}", format!("({} ms)", started.elapsed().as_millis()).dimmed());
                }
                if let Some(meta) = metadata.as_ref() {
                    print_citations(meta, verbosity);
                }
            }
            Err(e) => eprintln!("\n  {} {e:#}", "ERROR:".red().bold()),
        }

        println!();
    }

    Ok(())
}

fn require_app_id(app_id: &str) -> anyhow::Result<()> {
    if app_id.is_empty() {
        anyhow::bail!("--appid is required (or set WORKIQ_APP_ID)");
    }
    Ok(())
}

async fn run_subcommand(cmd: &Command, app_id: &str, cli: &Cli) -> anyhow::Result<()> {
    match cmd {
        Command::Login => {
            let mut mgr =
                AuthManager::new(app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, cli.account.as_deref()).await?;
            let token = mgr.get_token(cli.verbosity).await?;
            println!("\n{}", "Logged in successfully.".green().bold());
            if let Some(acct) = mgr.cached_account().await {
                println!("  Account: {}", acct.cyan());
            }
            if cli.verbosity >= 1 {
                log_header("TOKEN");
                decode_token(&token);
            }
        }
        Command::Logout => {
            let mgr = AuthManager::new(app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, None).await?;
            mgr.sign_out_all().await?;
            println!("{}", "Logged out.".green());
        }
        Command::Status => {
            let mut mgr =
                AuthManager::new(app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, cli.account.as_deref()).await?;
            if !mgr.has_accounts().await {
                println!(
                    "{}",
                    "No cached session. Run `workiq-a2a login` to authenticate.".yellow()
                );
                return Ok(());
            }
            println!("{}", "Cached session found.".green());
            println!("  Client ID: {}", app_id.dimmed());
            if let Some(acct) = mgr.cached_account().await {
                println!("  Account:   {}", acct.cyan());
            }
            match mgr.ensure_fresh(0).await {
                Ok(token) => {
                    decode_token(&token);
                    if cli.show_token {
                        println!("\n  {token}\n");
                    }
                }
                Err(_) => println!("  Token:     {}", "expired or unavailable".yellow()),
            }
        }
    }
    Ok(())
}

/// Build an A2A message with Location metadata (timezone info), matching the .NET CLI.
fn build_message(role: Role, text: &str, context_id: Option<String>) -> Message {
    let now = chrono::Local::now();
    let offset_minutes = now.offset().local_minus_utc() / 60;
    let tz_name = iana_time_zone::get_timezone().unwrap_or_else(|_| "Unknown".to_string());

    let metadata = serde_json::json!({
        "Location": {
            "timeZoneOffset": offset_minutes,
            "timeZone": tz_name,
        }
    });

    Message {
        kind: "message".to_string(),
        message_id: message_id(),
        context_id,
        task_id: None,
        role,
        parts: vec![Part::text(text)],
        metadata: Some(metadata),
        extensions: vec![],
        reference_task_ids: None,
    }
}

/// Generate a 32-hex-char message ID. Combines monotonic nanos with a process
/// counter so two calls in the same nanosecond still differ.
fn message_id() -> String {
    use std::sync::atomic::AtomicU64;
    use std::time::{SystemTime, UNIX_EPOCH};
    static COUNTER: AtomicU64 = AtomicU64::new(0);
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos() as u64)
        .unwrap_or(0);
    let n = COUNTER.fetch_add(1, Ordering::Relaxed);
    format!("{nanos:016x}{n:016x}")
}

// ── Sync handler ─────────────────────────────────────────────────────────

async fn handle_sync(
    client: &WorkIQClient,
    message: Message,
    config: Option<SendMessageConfiguration>,
    context_id: &mut Option<String>,
    spinner: &Spinner,
) -> anyhow::Result<Option<serde_json::Value>> {
    let result = client.send_message(message, config).await?;
    spinner.stop();
    let (text, ctx, metadata) = extract_result(&result);
    *context_id = ctx;
    println!("{text}");
    Ok(metadata)
}

fn extract_result(
    result: &SendMessageResult,
) -> (String, Option<String>, Option<serde_json::Value>) {
    match result {
        SendMessageResult::Task(task) => {
            // Answer text comes from artifacts. status.message carries
            // chain-of-thought / progress and citation metadata only.
            let text = join_artifact_text(task.artifacts.as_deref());
            let text = if text.is_empty() {
                format!("[Task {} — {:?}]", task.id, task.status.state)
            } else {
                text
            };
            let meta = task
                .status
                .message
                .as_ref()
                .and_then(|m| m.metadata.clone())
                .or_else(|| task.metadata.clone());
            (text, Some(task.context_id.clone()), meta)
        }
        SendMessageResult::Message(msg) => {
            let text = join_text_parts(&msg.parts);
            (text, msg.context_id.clone(), msg.metadata.clone())
        }
    }
}

fn join_text_parts(parts: &[Part]) -> String {
    parts.iter().filter_map(Part::as_text).collect::<Vec<_>>().join("\n")
}

fn join_artifact_text(artifacts: Option<&[a2a_rs_core::Artifact]>) -> String {
    artifacts
        .unwrap_or(&[])
        .iter()
        .flat_map(|a| a.parts.iter())
        .filter_map(Part::as_text)
        .collect::<Vec<_>>()
        .join("\n")
}

// ── Citations ────────────────────────────────────────────────────────────

fn print_citations(metadata: &serde_json::Value, verbosity: u8) {
    let attrs = match metadata.get("attributions").and_then(|v| v.as_array()) {
        Some(a) if !a.is_empty() => a,
        _ => return,
    };

    let citations: Vec<Citation> = attrs.iter().map(Citation::from_value).collect();
    let n_citations = citations.iter().filter(|c| c.is_citation()).count();
    let n_annotations = citations.iter().filter(|c| c.is_annotation()).count();

    if verbosity >= 1 {
        println!(
            "  {}",
            format!("Citations: {n_citations}  Annotations: {n_annotations}").yellow()
        );
    }
    if verbosity < 2 {
        return;
    }

    for c in &citations {
        let label = if c.is_citation() { "\u{1f4c4}" } else { "\u{1f517}" };
        let name = if c.provider.is_empty() { "(unnamed)" } else { c.provider.as_str() };
        let header = format!("{label} [{}/{}] {name}", c.attr_type, c.source);
        if c.is_citation() {
            println!("    {}", header.yellow());
        } else {
            println!("    {}", header.dimmed());
        }
        if !c.url.is_empty() {
            let truncated: String = if c.url.len() <= 120 {
                c.url.clone()
            } else {
                c.url.chars().take(120).collect()
            };
            println!("       {}", truncated.dimmed());
        }
    }
}

struct Citation {
    attr_type: String,
    source: String,
    provider: String,
    url: String,
}

impl Citation {
    fn from_value(v: &serde_json::Value) -> Self {
        let s = |k: &str| {
            v.get(k)
                .and_then(|x| x.as_str())
                .unwrap_or("")
                .to_string()
        };
        Self {
            attr_type: s("attributionType"),
            source: s("attributionSource"),
            provider: s("providerDisplayName"),
            url: s("seeMoreWebUrl"),
        }
    }

    fn is_citation(&self) -> bool {
        self.attr_type.to_ascii_lowercase().contains("citation")
    }

    fn is_annotation(&self) -> bool {
        self.attr_type.to_ascii_lowercase().contains("annotation")
    }
}

// ── Utilities ────────────────────────────────────────────────────────────

fn log_header(label: &str) {
    println!("\n{}", format!("── {label} ──").dimmed());
}

// ── Spinner ──────────────────────────────────────────────────────────────

struct Spinner {
    running: Arc<AtomicBool>,
    handle: Option<std::thread::JoinHandle<()>>,
}

impl Spinner {
    fn start() -> Self {
        const FRAMES: &[&str] = &[
            "\u{b7}  ",
            "\u{b7}\u{b7} ",
            "\u{b7}\u{b7}\u{b7}",
            " \u{b7}\u{b7}",
            "  \u{b7}",
            "   ",
        ];
        let running = Arc::new(AtomicBool::new(true));
        let r = running.clone();
        let handle = std::thread::spawn(move || {
            let mut i = 0;
            while r.load(Ordering::Relaxed) {
                print!("{}", FRAMES[i % FRAMES.len()]);
                let _ = io::stdout().flush();
                std::thread::sleep(std::time::Duration::from_millis(150));
                print!("\x08\x08\x08");
                let _ = io::stdout().flush();
                i += 1;
            }
            print!("   \x08\x08\x08");
            let _ = io::stdout().flush();
        });
        Self { running, handle: Some(handle) }
    }

    fn stop(&self) {
        self.running.store(false, Ordering::Relaxed);
    }
}

impl Drop for Spinner {
    fn drop(&mut self) {
        self.running.store(false, Ordering::Relaxed);
        if let Some(h) = self.handle.take() {
            let _ = h.join();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // ── message_id ──────────────────────────────────────────────────

    #[test]
    fn message_id_is_32_hex() {
        let id = message_id();
        assert_eq!(id.len(), 32, "expected 32 hex chars, got {}", id.len());
        assert!(id.chars().all(|c| c.is_ascii_hexdigit()), "non-hex char in: {id}");
    }

    #[test]
    fn message_id_unique_within_same_nanosecond() {
        let a = message_id();
        let b = message_id();
        assert_ne!(a, b, "consecutive IDs should differ via the counter");
    }

    // ── build_message ───────────────────────────────────────────────

    #[test]
    fn build_message_structure() {
        let msg = build_message(Role::User, "hello", None);
        assert_eq!(msg.kind, "message");
        assert_eq!(msg.role, Role::User);
        assert_eq!(msg.parts.len(), 1);
        assert_eq!(msg.parts[0].as_text(), Some("hello"));
        assert!(msg.context_id.is_none());
        assert_eq!(msg.message_id.len(), 32);
    }

    #[test]
    fn build_message_with_context() {
        let msg = build_message(Role::Agent, "reply", Some("ctx-42".into()));
        assert_eq!(msg.context_id.as_deref(), Some("ctx-42"));
        assert_eq!(msg.role, Role::Agent);
    }

    #[test]
    fn build_message_has_location_metadata() {
        let msg = build_message(Role::User, "test", None);
        let meta = msg.metadata.as_ref().expect("metadata missing");
        let loc = &meta["Location"];
        assert!(loc.get("timeZoneOffset").is_some(), "missing timeZoneOffset");
        assert!(loc.get("timeZone").is_some(), "missing timeZone");
    }

    #[test]
    fn build_message_empty_text() {
        let msg = build_message(Role::User, "", None);
        match &msg.parts[0] {
            Part::Text { text, .. } => assert_eq!(text, ""),
            other => panic!("expected Text part, got {other:?}"),
        }
    }

    #[test]
    fn build_message_special_characters() {
        let input = "He said \"hello\"\nnew line\ttab \u{1f680} caf\u{e9}";
        let msg = build_message(Role::User, input, None);
        match &msg.parts[0] {
            Part::Text { text, .. } => assert_eq!(text, input),
            other => panic!("expected Text part, got {other:?}"),
        }
    }

    // ── join_text_parts ─────────────────────────────────────────────

    #[test]
    fn join_text_parts_basic() {
        let parts = vec![Part::text("hello"), Part::text("world")];
        assert_eq!(join_text_parts(&parts), "hello\nworld");
    }

    #[test]
    fn join_text_parts_skips_non_text() {
        let parts = vec![
            Part::text("a"),
            Part::data(serde_json::json!({})),
            Part::text("b"),
        ];
        assert_eq!(join_text_parts(&parts), "a\nb");
    }

    #[test]
    fn join_text_parts_empty() {
        assert_eq!(join_text_parts(&[]), "");
    }

    #[test]
    fn join_text_parts_single() {
        let parts = vec![Part::text("only")];
        assert_eq!(join_text_parts(&parts), "only");
    }

    #[test]
    fn join_text_parts_with_newlines_in_text() {
        let parts = vec![Part::text("line1\nline2"), Part::text("line3\nline4")];
        assert_eq!(join_text_parts(&parts), "line1\nline2\nline3\nline4");
    }

    // ── extract_result ──────────────────────────────────────────────

    #[test]
    fn extract_result_from_message() {
        let msg = Message {
            kind: "message".into(),
            message_id: "m1".into(),
            context_id: Some("ctx-1".into()),
            task_id: None,
            role: Role::Agent,
            parts: vec![Part::text("response")],
            metadata: Some(serde_json::json!({"key":"val"})),
            extensions: vec![],
            reference_task_ids: None,
        };
        let (text, ctx, meta) = extract_result(&SendMessageResult::Message(msg));
        assert_eq!(text, "response");
        assert_eq!(ctx.as_deref(), Some("ctx-1"));
        assert_eq!(meta.unwrap()["key"], "val");
    }

    fn artifact(id: &str, parts: Vec<Part>) -> a2a_rs_core::Artifact {
        a2a_rs_core::Artifact {
            artifact_id: id.into(),
            name: None,
            description: None,
            parts,
            metadata: None,
            extensions: vec![],
        }
    }

    fn task_with(
        id: &str,
        ctx: &str,
        state: a2a_rs_core::TaskState,
        artifacts: Option<Vec<a2a_rs_core::Artifact>>,
        status_message: Option<Message>,
    ) -> a2a_rs_core::Task {
        a2a_rs_core::Task {
            kind: "task".into(),
            id: id.into(),
            context_id: ctx.into(),
            status: a2a_rs_core::TaskStatus {
                state,
                message: status_message,
                timestamp: None,
            },
            artifacts,
            history: None,
            metadata: None,
        }
    }

    #[test]
    fn extract_result_from_task_uses_artifacts() {
        // Answer text comes from artifacts, not status.message.
        let task = task_with(
            "t1",
            "ctx-2",
            a2a_rs_core::TaskState::Completed,
            Some(vec![artifact("a1", vec![Part::text("Answer from artifact")])]),
            None,
        );
        let (text, ctx, _meta) = extract_result(&SendMessageResult::Task(task));
        assert_eq!(text, "Answer from artifact");
        assert_eq!(ctx.as_deref(), Some("ctx-2"));
    }

    #[test]
    fn extract_result_task_concatenates_multiple_artifacts() {
        let task = task_with(
            "t-multi",
            "ctx-multi",
            a2a_rs_core::TaskState::Completed,
            Some(vec![
                artifact("a1", vec![Part::text("One")]),
                artifact("a2", vec![Part::text("Two")]),
            ]),
            None,
        );
        let (text, _, _) = extract_result(&SendMessageResult::Task(task));
        assert_eq!(text, "One\nTwo");
    }

    #[test]
    fn extract_result_task_picks_metadata_from_status_message() {
        // status.message carries citation metadata even though answer text
        // lives in artifacts.
        let status_msg = Message {
            kind: "message".into(),
            message_id: "m-cite".into(),
            context_id: None,
            task_id: None,
            role: Role::Agent,
            parts: vec![],
            metadata: Some(serde_json::json!({"cite": true})),
            extensions: vec![],
            reference_task_ids: None,
        };
        let task = task_with(
            "t-cite",
            "ctx-cite",
            a2a_rs_core::TaskState::Completed,
            Some(vec![artifact("a1", vec![Part::text("hello")])]),
            Some(status_msg),
        );
        let (text, _, meta) = extract_result(&SendMessageResult::Task(task));
        assert_eq!(text, "hello");
        assert_eq!(meta.unwrap()["cite"], true);
    }

    #[test]
    fn extract_result_task_no_artifacts_returns_placeholder() {
        // status.message text is intentionally ignored — that's chain-of-thought,
        // not the final answer.
        let task = task_with(
            "t-no-art",
            "ctx-3",
            a2a_rs_core::TaskState::Working,
            None,
            None,
        );
        let (text, ctx, _) = extract_result(&SendMessageResult::Task(task));
        assert!(text.contains("t-no-art"));
        assert!(text.contains("Working"));
        assert_eq!(ctx.as_deref(), Some("ctx-3"));
    }

    #[test]
    fn extract_result_task_status_message_text_ignored() {
        // status.message.parts has text but artifacts is empty: answer text
        // is empty (we don't fall back to status.message), so we get the
        // placeholder.
        let status_msg = Message {
            kind: "message".into(),
            message_id: "m-thought".into(),
            context_id: None,
            task_id: None,
            role: Role::Agent,
            parts: vec![Part::text("ignored chain-of-thought")],
            metadata: None,
            extensions: vec![],
            reference_task_ids: None,
        };
        let task = task_with(
            "t-thought",
            "ctx-thought",
            a2a_rs_core::TaskState::Completed,
            None,
            Some(status_msg),
        );
        let (text, _, _) = extract_result(&SendMessageResult::Task(task));
        assert!(text.contains("t-thought"));
        assert!(text.contains("Completed"));
    }

    #[test]
    fn extract_result_task_data_only_artifacts_returns_placeholder() {
        let task = task_with(
            "t-data",
            "ctx-data",
            a2a_rs_core::TaskState::Completed,
            Some(vec![artifact("a1", vec![Part::data(serde_json::json!({"k":"v"}))])]),
            None,
        );
        let (text, _, _) = extract_result(&SendMessageResult::Task(task));
        assert!(text.contains("t-data"));
    }

    // ── Citation ────────────────────────────────────────────────────

    #[test]
    fn citation_classifies_types() {
        let v = serde_json::json!({
            "attributionType": "DocCitation",
            "attributionSource": "doc.pdf",
            "providerDisplayName": "Provider",
            "seeMoreWebUrl": "https://x"
        });
        let c = Citation::from_value(&v);
        assert!(c.is_citation());
        assert!(!c.is_annotation());
        assert_eq!(c.url, "https://x");
    }

    #[test]
    fn citation_handles_missing_fields() {
        let c = Citation::from_value(&serde_json::json!({}));
        assert!(!c.is_citation());
        assert!(!c.is_annotation());
        assert_eq!(c.url, "");
    }
}
