mod a2a;
mod auth;
mod config;

use a2a::WorkIQClient;
use a2a_rs_core::{Message, Part, Role, SendMessageConfiguration, SendMessageResult, StreamingMessageResult};
use auth::{decode_token, AuthManager};
use clap::Parser;
use colored::Colorize;
use config::{Cli, Command, WORKIQ_AUTHORITY, WORKIQ_ENDPOINT, WORKIQ_SCOPES};
use futures_util::StreamExt;

use std::io::{self, Write};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Instant;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();
    let verbosity = cli.verbosity;

    let app_id = cli.appid.clone().unwrap_or_default();

    fn require_app_id(app_id: &str) -> anyhow::Result<()> {
        if app_id.is_empty() {
            anyhow::bail!("--appid is required (or set WORKIQ_APP_ID)");
        }
        Ok(())
    }

    // ── Handle subcommands ───────────────────────────────────────────
    match cli.command {
        Some(Command::Login) => {
            require_app_id(&app_id)?;
            let mut mgr =
                AuthManager::new(&app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, cli.account.as_deref()).await?;
            let token = mgr.get_token(verbosity).await?;
            println!("\n{}", "Logged in successfully.".green().bold());
            if let Some(acct) = mgr.cached_account().await {
                println!("  Account: {}", acct.cyan());
            }
            if verbosity >= 1 {
                log_header("TOKEN");
                decode_token(&token);
            }
            return Ok(());
        }
        Some(Command::Logout) => {
            require_app_id(&app_id)?;
            let mgr =
                AuthManager::new(&app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, None).await?;
            mgr.sign_out_all().await?;
            println!("{}", "Logged out.".green());
            return Ok(());
        }
        Some(Command::Status) => {
            require_app_id(&app_id)?;
            let mut mgr =
                AuthManager::new(&app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, cli.account.as_deref()).await?;
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
                Err(_) => {
                    println!("  Token:     {}", "expired or unavailable".yellow());
                }
            }
            return Ok(());
        }
        None => {}
    }

    // ── Resolve token for REPL ───────────────────────────────────────
    let (mut token, mut auth_mgr) = if let Some(ref raw_token) = cli.token {
        (raw_token.clone(), None)
    } else {
        require_app_id(&app_id)?;
        let mut mgr =
            AuthManager::new(&app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, cli.account.as_deref()).await?;
        let token = mgr.get_token(verbosity).await?;
        (token, Some(mgr))
    };

    // ── Display token info ───────────────────────────────────────────
    if verbosity >= 1 {
        log_header("TOKEN");
        decode_token(&token);
        if cli.show_token {
            println!("\n  {token}\n");
        }
    }

    // ── Set up A2A client ────────────────────────────────────────────
    let endpoint = cli.endpoint.as_deref().unwrap_or(WORKIQ_ENDPOINT);
    let mut client = WorkIQClient::new(endpoint, &token, &cli.headers)?;
    let mut context_id: Option<String> = None;

    if verbosity >= 1 {
        let mode = if cli.stream { "Streaming" } else { "Sync" };
        log_header(&format!("READY — Graph RP — {mode} — {endpoint}"));
        if let Some(ref mgr) = auth_mgr {
            if let Some(acct) = mgr.cached_account().await {
                println!("  Signed in as {}", acct.cyan());
            }
        }
        println!("Type a message. 'quit' to exit.\n");
    }

    // ── Interactive REPL ─────────────────────────────────────────────
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

        // Silent token refresh
        if let Some(ref mut mgr) = auth_mgr {
            if let Ok(fresh_token) = mgr.ensure_fresh(verbosity).await {
                if fresh_token != token {
                    token = fresh_token;
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

        let config = Some(SendMessageConfiguration {
            accepted_output_modes: Some(vec!["text/plain".to_string()]),
            blocking: Some(!cli.stream),
            history_length: None,
            push_notification_config: None,
            return_immediately: None,
        });

        let sw = Instant::now();

        if cli.stream {
            match handle_streaming(&client, message, config, &mut context_id, verbosity, &spinner)
                .await
            {
                Ok(metadata) => {
                    spinner.stop();
                    let elapsed = sw.elapsed().as_millis();
                    if verbosity >= 1 {
                        println!("  {}", format!("({elapsed} ms)").dimmed());
                    }
                    if let Some(ref meta) = metadata {
                        print_citations(meta, verbosity);
                    }
                }
                Err(e) => {
                    spinner.stop();
                    eprintln!("\n  {} {}: {}", "ERROR:".red().bold(), short_type(&e), e);
                }
            }
        } else {
            match handle_sync(&client, message, config, &mut context_id, verbosity, &spinner).await
            {
                Ok(metadata) => {
                    spinner.stop();
                    let elapsed = sw.elapsed().as_millis();
                    if verbosity >= 1 {
                        println!("  {}", format!("({elapsed} ms)").dimmed());
                    }
                    if let Some(ref meta) = metadata {
                        print_citations(meta, verbosity);
                    }
                }
                Err(e) => {
                    spinner.stop();
                    eprintln!("\n  {} {}: {}", "ERROR:".red().bold(), short_type(&e), e);
                }
            }
        }

        println!();
    }

    Ok(())
}

/// Build a message with Location metadata (timezone info), matching .NET CLI.
fn build_message(role: Role, text: &str, context_id: Option<String>) -> Message {
    let now = chrono::Local::now();
    let offset_minutes = now.offset().local_minus_utc() / 60;
    let tz_name = iana_time_zone::get_timezone().unwrap_or_else(|_| "Unknown".to_string());

    let location = serde_json::json!({
        "timeZoneOffset": offset_minutes,
        "timeZone": tz_name,
    });
    let metadata = serde_json::json!({ "Location": location });

    Message {
        kind: "message".to_string(),
        message_id: uuid_v4(),
        context_id,
        task_id: None,
        role,
        parts: vec![Part::Text {
            text: text.to_string(),
            metadata: None,
        }],
        metadata: Some(metadata),
        extensions: vec![],
        reference_task_ids: None,
    }
}

fn uuid_v4() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let t = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_nanos();
    // Simple unique ID; not cryptographically random but sufficient for message IDs
    format!("{:032x}", t)
}

// ── Sync handler ─────────────────────────────────────────────────────────

async fn handle_sync(
    client: &WorkIQClient,
    message: Message,
    config: Option<SendMessageConfiguration>,
    context_id: &mut Option<String>,
    _verbosity: u8,
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
            let text = task
                .status
                .message
                .as_ref()
                .map(|m| join_text_parts(&m.parts))
                .unwrap_or_else(|| format!("[Task {} — {:?}]", task.id, task.status.state));
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
    parts
        .iter()
        .filter_map(|p| match p {
            Part::Text { text, .. } => Some(text.as_str()),
            _ => None,
        })
        .collect::<Vec<_>>()
        .join("\n")
}

// ── Streaming handler ────────────────────────────────────────────────────

async fn handle_streaming(
    client: &WorkIQClient,
    message: Message,
    config: Option<SendMessageConfiguration>,
    context_id: &mut Option<String>,
    verbosity: u8,
    spinner: &Spinner,
) -> anyhow::Result<Option<serde_json::Value>> {
    let mut stream = client.send_message_streaming(message, config).await?;
    let mut previous_text = String::new();
    let mut response_metadata: Option<serde_json::Value> = None;

    while let Some(result) = stream.next().await {
        match result {
            Ok(event) => match event {
                StreamingMessageResult::Task(task) => {
                    *context_id = Some(task.context_id.clone());
                    if let Some(ref msg) = task.status.message {
                        response_metadata = msg.metadata.clone();

                        if verbosity >= 1 {
                            print_event_details(&msg.parts, &format!("{:?}", task.status.state));
                        }

                        let combined = join_text_parts(&msg.parts);
                        spinner.stop();
                        print_delta(&combined, &mut previous_text);
                    }
                    if task.status.state.is_terminal() {
                        break;
                    }
                }
                StreamingMessageResult::Message(msg) => {
                    response_metadata = msg.metadata.clone();
                    let combined = join_text_parts(&msg.parts);
                    spinner.stop();
                    print_delta(&combined, &mut previous_text);
                }
                StreamingMessageResult::StatusUpdate(evt) => {
                    *context_id = Some(evt.context_id.clone());
                    if let Some(ref msg) = evt.status.message {
                        response_metadata = msg.metadata.clone();

                        if verbosity >= 1 {
                            print_event_details(&msg.parts, &format!("{:?}", evt.status.state));
                        }

                        let combined = join_text_parts(&msg.parts);
                        spinner.stop();
                        print_delta(&combined, &mut previous_text);
                    }
                    if evt.status.state.is_terminal() || evt.is_final {
                        break;
                    }
                }
                StreamingMessageResult::ArtifactUpdate(evt) => {
                    if let Some(ref name) = evt.artifact.name {
                        print!(" [{}]", name.dimmed());
                    }
                    let combined = join_text_parts(&evt.artifact.parts);
                    spinner.stop();
                    print_delta(&combined, &mut previous_text);
                }
            },
            Err(e) => {
                spinner.stop();
                eprintln!("\n{} {}", "Stream error:".red(), e);
                break;
            }
        }
    }

    println!();
    Ok(response_metadata)
}

/// Print only the new text since the last update (delta printing).
fn print_delta(combined: &str, previous_text: &mut String) {
    if combined.starts_with(previous_text.as_str()) {
        print!("{}", &combined[previous_text.len()..]);
    } else {
        print!("{combined}");
    }
    *previous_text = combined.to_string();
    let _ = io::stdout().flush();
}

/// Log streaming event details at verbosity >= 1 (part types and sizes).
fn print_event_details(parts: &[Part], state: &str) {
    let part_descs: Vec<String> = parts
        .iter()
        .map(|p| match p {
            Part::Text { text, .. } => format!("TextPart({}c)", text.len()),
            Part::Data { .. } => "DataPart".to_string(),
            Part::File { file, .. } => {
                let name = file.name.as_deref().unwrap_or("?");
                format!("FilePart({name})")
            }
        })
        .collect();
    eprintln!("  {}", format!("  [{state}] {}", part_descs.join(" + ")).dimmed());
}

// ── Citations ────────────────────────────────────────────────────────────

fn print_citations(metadata: &serde_json::Value, verbosity: u8) {
    let attrs = match metadata.get("attributions").and_then(|v| v.as_array()) {
        Some(a) if !a.is_empty() => a,
        _ => return,
    };

    let mut citation_count = 0u32;
    let mut annotation_count = 0u32;

    struct Citation {
        attr_type: String,
        source: String,
        provider: String,
        url: String,
    }

    let mut citations = Vec::new();

    for attr in attrs {
        let attr_type = attr
            .get("attributionType")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let source = attr
            .get("attributionSource")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let provider = attr
            .get("providerDisplayName")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let url = attr
            .get("seeMoreWebUrl")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();

        if attr_type.to_lowercase().contains("citation") {
            citation_count += 1;
        }
        if attr_type.to_lowercase().contains("annotation") {
            annotation_count += 1;
        }

        citations.push(Citation {
            attr_type,
            source,
            provider,
            url,
        });
    }

    if citations.is_empty() {
        return;
    }

    if verbosity >= 1 {
        println!(
            "  {}",
            format!("Citations: {citation_count}  Annotations: {annotation_count}").yellow()
        );
    }

    if verbosity >= 2 {
        for c in &citations {
            let is_citation = c.attr_type.to_lowercase().contains("citation");
            let label = if is_citation { "\u{1f4c4}" } else { "\u{1f517}" };
            let name = if c.provider.is_empty() {
                "(unnamed)"
            } else {
                &c.provider
            };
            if is_citation {
                println!(
                    "    {}",
                    format!("{label} [{}/{}] {name}", c.attr_type, c.source).yellow()
                );
            } else {
                println!(
                    "    {}",
                    format!("{label} [{}/{}] {name}", c.attr_type, c.source).dimmed()
                );
            }
            if !c.url.is_empty() {
                let truncated = if c.url.len() <= 120 {
                    &c.url
                } else {
                    &c.url[..120]
                };
                println!("       {}", truncated.dimmed());
            }
        }
    }
}

// ── Utilities ────────────────────────────────────────────────────────────

fn log_header(label: &str) {
    println!("\n{}", format!("── {label} ──").dimmed());
}

/// Get a short type name from an anyhow error for display.
fn short_type(e: &anyhow::Error) -> String {
    let debug = format!("{e:?}");
    // Extract the first type-like token from the debug output
    if let Some(end) = debug.find('(').or_else(|| debug.find(':')) {
        debug[..end].trim().to_string()
    } else {
        "Error".to_string()
    }
}

// ── Spinner ──────────────────────────────────────────────────────────────

struct Spinner {
    running: Arc<AtomicBool>,
    handle: Option<std::thread::JoinHandle<()>>,
}

impl Spinner {
    fn start() -> Self {
        let running = Arc::new(AtomicBool::new(true));
        let r = running.clone();
        let handle = std::thread::spawn(move || {
            const FRAMES: &[&str] = &["\u{b7}  ", "\u{b7}\u{b7} ", "\u{b7}\u{b7}\u{b7}", " \u{b7}\u{b7}", "  \u{b7}", "   "];
            let mut i = 0;
            while r.load(Ordering::Relaxed) {
                let frame = FRAMES[i % FRAMES.len()];
                print!("{frame}");
                let _ = io::stdout().flush();
                std::thread::sleep(std::time::Duration::from_millis(150));
                // Move cursor back
                print!("\x08\x08\x08");
                let _ = io::stdout().flush();
                i += 1;
            }
            // Clear spinner area
            print!("   \x08\x08\x08");
            let _ = io::stdout().flush();
        });
        Self {
            running,
            handle: Some(handle),
        }
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

    // ── uuid_v4 ─────────────────────────────────────────────────────

    #[test]
    fn uuid_v4_format() {
        let id = uuid_v4();
        assert_eq!(id.len(), 32, "expected 32 hex chars, got {}", id.len());
        assert!(
            id.chars().all(|c| c.is_ascii_hexdigit()),
            "non-hex char in: {id}"
        );
    }

    #[test]
    fn uuid_v4_uniqueness() {
        let a = uuid_v4();
        // Small sleep so the nanos-based ID advances
        std::thread::sleep(std::time::Duration::from_millis(1));
        let b = uuid_v4();
        assert_ne!(a, b, "IDs should differ with a time gap");
    }

    // ── build_message ───────────────────────────────────────────────

    #[test]
    fn build_message_structure() {
        let msg = build_message(Role::User, "hello", None);
        assert_eq!(msg.kind, "message");
        assert_eq!(msg.role, Role::User);
        assert_eq!(msg.parts.len(), 1);
        match &msg.parts[0] {
            Part::Text { text, .. } => assert_eq!(text, "hello"),
            other => panic!("expected Text part, got {other:?}"),
        }
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

    // ── join_text_parts ─────────────────────────────────────────────

    #[test]
    fn join_text_parts_basic() {
        let parts = vec![
            Part::Text {
                text: "hello".into(),
                metadata: None,
            },
            Part::Text {
                text: "world".into(),
                metadata: None,
            },
        ];
        assert_eq!(join_text_parts(&parts), "hello\nworld");
    }

    #[test]
    fn join_text_parts_skips_non_text() {
        let parts = vec![
            Part::Text {
                text: "a".into(),
                metadata: None,
            },
            Part::Data {
                data: serde_json::json!({}),
                metadata: None,
            },
            Part::Text {
                text: "b".into(),
                metadata: None,
            },
        ];
        assert_eq!(join_text_parts(&parts), "a\nb");
    }

    #[test]
    fn join_text_parts_empty() {
        assert_eq!(join_text_parts(&[]), "");
    }

    #[test]
    fn join_text_parts_single() {
        let parts = vec![Part::Text {
            text: "only".into(),
            metadata: None,
        }];
        assert_eq!(join_text_parts(&parts), "only");
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
            parts: vec![Part::Text {
                text: "response".into(),
                metadata: None,
            }],
            metadata: Some(serde_json::json!({"key": "val"})),
            extensions: vec![],
            reference_task_ids: None,
        };
        let result = SendMessageResult::Message(msg);
        let (text, ctx, meta) = extract_result(&result);
        assert_eq!(text, "response");
        assert_eq!(ctx.as_deref(), Some("ctx-1"));
        assert_eq!(meta.unwrap()["key"], "val");
    }

    #[test]
    fn extract_result_from_task() {
        let task = a2a_rs_core::Task {
            kind: "task".into(),
            id: "t1".into(),
            context_id: "ctx-2".into(),
            status: a2a_rs_core::TaskStatus {
                state: a2a_rs_core::TaskState::Completed,
                message: Some(Message {
                    kind: "message".into(),
                    message_id: "m2".into(),
                    context_id: None,
                    task_id: None,
                    role: Role::Agent,
                    parts: vec![Part::Text {
                        text: "done".into(),
                        metadata: None,
                    }],
                    metadata: Some(serde_json::json!({"cite": true})),
                    extensions: vec![],
                    reference_task_ids: None,
                }),
                timestamp: None,
            },
            artifacts: None,
            history: None,
            metadata: None,
        };
        let result = SendMessageResult::Task(task);
        let (text, ctx, meta) = extract_result(&result);
        assert_eq!(text, "done");
        assert_eq!(ctx.as_deref(), Some("ctx-2"));
        assert_eq!(meta.unwrap()["cite"], true);
    }

    #[test]
    fn extract_result_task_without_message() {
        let task = a2a_rs_core::Task {
            kind: "task".into(),
            id: "t2".into(),
            context_id: "ctx-3".into(),
            status: a2a_rs_core::TaskStatus {
                state: a2a_rs_core::TaskState::Working,
                message: None,
                timestamp: None,
            },
            artifacts: None,
            history: None,
            metadata: None,
        };
        let result = SendMessageResult::Task(task);
        let (text, ctx, _meta) = extract_result(&result);
        assert!(text.contains("t2"), "expected task id in fallback: {text}");
        assert!(text.contains("Working"), "expected state in fallback: {text}");
        assert_eq!(ctx.as_deref(), Some("ctx-3"));
    }

    // ── print_delta ─────────────────────────────────────────────────

    #[test]
    fn print_delta_incremental() {
        let mut prev = String::new();

        // First call — entire text is new
        print_delta("Hello", &mut prev);
        assert_eq!(prev, "Hello");

        // Second call — only " world" is new
        print_delta("Hello world", &mut prev);
        assert_eq!(prev, "Hello world");
    }

    #[test]
    fn print_delta_full_replace() {
        let mut prev = "old text".to_string();
        // New text doesn't start with old — prints full new text
        print_delta("completely new", &mut prev);
        assert_eq!(prev, "completely new");
    }

    #[test]
    fn print_delta_empty() {
        let mut prev = String::new();
        print_delta("", &mut prev);
        assert_eq!(prev, "");
    }

    // ── edge-case tests ─────────────────────────────────────────────

    #[test]
    fn build_message_empty_text() {
        let msg = build_message(Role::User, "", None);
        assert_eq!(msg.parts.len(), 1);
        match &msg.parts[0] {
            Part::Text { text, .. } => assert_eq!(text, ""),
            other => panic!("expected Text part, got {other:?}"),
        }
        assert_eq!(msg.kind, "message");
    }

    #[test]
    fn build_message_special_characters() {
        let input = "He said \"hello\"\nnew line\ttab 🚀 café";
        let msg = build_message(Role::User, input, None);
        match &msg.parts[0] {
            Part::Text { text, .. } => assert_eq!(text, input),
            other => panic!("expected Text part, got {other:?}"),
        }
    }

    #[test]
    fn extract_result_from_task_completed_empty_message() {
        let task = a2a_rs_core::Task {
            kind: "task".into(),
            id: "t-empty".into(),
            context_id: "ctx-e".into(),
            status: a2a_rs_core::TaskStatus {
                state: a2a_rs_core::TaskState::Completed,
                message: Some(Message {
                    kind: "message".into(),
                    message_id: "m-empty".into(),
                    context_id: None,
                    task_id: None,
                    role: Role::Agent,
                    parts: vec![], // no text parts
                    metadata: None,
                    extensions: vec![],
                    reference_task_ids: None,
                }),
                timestamp: None,
            },
            artifacts: None,
            history: None,
            metadata: None,
        };
        let result = SendMessageResult::Task(task);
        let (text, ctx, _meta) = extract_result(&result);
        assert_eq!(text, "", "empty parts should produce empty string");
        assert_eq!(ctx.as_deref(), Some("ctx-e"));
    }

    #[test]
    fn join_text_parts_with_newlines_in_text() {
        let parts = vec![
            Part::Text {
                text: "line1\nline2".into(),
                metadata: None,
            },
            Part::Text {
                text: "line3\nline4".into(),
                metadata: None,
            },
        ];
        // Parts are joined with \n, so embedded newlines are preserved alongside the separator.
        assert_eq!(join_text_parts(&parts), "line1\nline2\nline3\nline4");
    }
}
