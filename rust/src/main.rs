mod a2a;
mod auth;
mod config;

use a2a::WorkIQClient;
use a2a_rs_core::{
    new_message, Part, Role, SendMessageConfiguration, SendMessageResult,
    StreamingMessageResult,
};
use auth::{decode_token, AuthManager, TokenCache};
use clap::Parser;
use colored::Colorize;
use config::{Cli, Command, WORKIQ_AUTHORITY, WORKIQ_ENDPOINT, WORKIQ_EXTRA_HEADERS, WORKIQ_SCOPES};
use futures_util::StreamExt;

use std::io::{self, Write};

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
                AuthManager::new(&app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, cli.account.as_deref());
            let token = mgr.get_token(verbosity).await?;
            println!("\n{}", "Logged in successfully.".green().bold());
            if let Some(acct) = mgr.cached_account() {
                println!("  Account: {}", acct.cyan());
            }
            if verbosity >= 1 {
                log_header("TOKEN");
                decode_token(&token);
            }
            return Ok(());
        }
        Some(Command::Logout) => {
            TokenCache::clear()?;
            println!("{}", "Logged out. Token cache cleared.".green());
            return Ok(());
        }
        Some(Command::Status) => {
            match TokenCache::load() {
                Some(cache) => {
                    println!("{}", "Cached session found.".green());
                    println!("  Client ID: {}", cache.client_id.dimmed());
                    if let Some(ref acct) = cache.account {
                        println!("  Account:   {}", acct.cyan());
                    }
                    if cache.is_expired() {
                        println!("  Token:     {}", "EXPIRED".red().bold());
                        if cache.refresh_token.is_some() {
                            println!(
                                "  {}",
                                "Refresh token available — will auto-renew on next use.".dimmed()
                            );
                        }
                    } else {
                        let remaining = cache.expires_at - chrono::Utc::now().timestamp();
                        println!(
                            "  Token:     {} ({}m {}s remaining)",
                            "valid".green(),
                            remaining / 60,
                            remaining % 60
                        );
                    }
                    if cli.show_token {
                        println!("\n  {}\n", cache.access_token);
                    }
                }
                None => {
                    println!(
                        "{}",
                        "No cached session. Run `workiq-a2a login` to authenticate.".yellow()
                    );
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
            AuthManager::new(&app_id, WORKIQ_SCOPES, WORKIQ_AUTHORITY, cli.account.as_deref());
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
    let mut client = WorkIQClient::new(WORKIQ_ENDPOINT, &token, WORKIQ_EXTRA_HEADERS)?;
    let mut context_id: Option<String> = None;

    if verbosity >= 1 {
        let mode = if cli.stream { "Streaming" } else { "Sync" };
        log_header(&format!("READY — WorkIQ — {mode} — {WORKIQ_ENDPOINT}"));
        if let Some(ref mgr) = auth_mgr {
            if let Some(acct) = mgr.cached_account() {
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

        let message = new_message(Role::User, input, context_id.clone());

        let config = Some(SendMessageConfiguration {
            accepted_output_modes: Some(vec!["text/plain".to_string()]),
            blocking: Some(!cli.stream),
            history_length: None,
            push_notification_config: None,
            return_immediately: None,
        });

        if cli.stream {
            handle_streaming(&client, message, config, &mut context_id, verbosity).await?;
        } else {
            handle_sync(&client, message, config, &mut context_id, verbosity).await?;
        }
    }

    Ok(())
}

async fn handle_sync(
    client: &WorkIQClient,
    message: a2a_rs_core::Message,
    config: Option<SendMessageConfiguration>,
    context_id: &mut Option<String>,
    verbosity: u8,
) -> anyhow::Result<()> {
    match client.send_message(message, config).await {
        Ok(result) => display_result(&result, context_id, verbosity),
        Err(e) => eprintln!("{} {}\n", "Error:".red().bold(), e),
    }
    Ok(())
}

async fn handle_streaming(
    client: &WorkIQClient,
    message: a2a_rs_core::Message,
    config: Option<SendMessageConfiguration>,
    context_id: &mut Option<String>,
    verbosity: u8,
) -> anyhow::Result<()> {
    if verbosity >= 1 {
        print!("{}", "Agent > ".green());
        io::stdout().flush()?;
    }

    match client.send_message_streaming(message, config).await {
        Ok(mut stream) => {
            while let Some(result) = stream.next().await {
                match result {
                    Ok(event) => match event {
                        StreamingMessageResult::Task(task) => {
                            *context_id = Some(task.context_id.clone());
                            if let Some(ref msg) = task.status.message {
                                print_parts_inline(&msg.parts);
                            }
                            if task.status.state.is_terminal() {
                                println!();
                            }
                        }
                        StreamingMessageResult::Message(msg) => {
                            print_parts_inline(&msg.parts);
                        }
                        StreamingMessageResult::StatusUpdate(evt) => {
                            *context_id = Some(evt.context_id.clone());
                            if let Some(ref msg) = evt.status.message {
                                print_parts_inline(&msg.parts);
                            }
                            if evt.status.state.is_terminal() || evt.is_final {
                                println!();
                            }
                        }
                        StreamingMessageResult::ArtifactUpdate(evt) => {
                            if let Some(ref name) = evt.artifact.name {
                                print!(" [{}]", name.dimmed());
                            }
                            print_parts_inline(&evt.artifact.parts);
                            let _ = io::stdout().flush();
                        }
                    },
                    Err(e) => {
                        eprintln!("\n{} {}", "Stream error:".red(), e);
                        break;
                    }
                }
            }
        }
        Err(e) => {
            eprintln!("{} {}", "Error:".red().bold(), e);
        }
    }

    println!();
    Ok(())
}

fn display_result(
    result: &SendMessageResult,
    context_id: &mut Option<String>,
    verbosity: u8,
) {
    match result {
        SendMessageResult::Task(task) => {
            *context_id = Some(task.context_id.clone());

            if verbosity >= 1 {
                print!("{}", "Agent > ".green());
            }

            if let Some(ref msg) = task.status.message {
                print_parts(&msg.parts);
            }

            if let Some(ref artifacts) = task.artifacts {
                for artifact in artifacts {
                    if let Some(ref name) = artifact.name {
                        println!("  {}: {}", "Artifact".dimmed(), name);
                    }
                    print_parts(&artifact.parts);
                }
            }

            if verbosity >= 2 {
                println!(
                    "  {} task={} context={} state={:?}",
                    "Meta:".dimmed(),
                    task.id,
                    task.context_id,
                    task.status.state
                );
            }

            println!();
        }
        SendMessageResult::Message(msg) => {
            if verbosity >= 1 {
                print!("{}", "Agent > ".green());
            }
            print_parts(&msg.parts);
            println!();
        }
    }
}

fn print_parts(parts: &[Part]) {
    for part in parts {
        match part {
            Part::Text { text, .. } => println!("{text}"),
            Part::Data { data, .. } => {
                println!("{}", serde_json::to_string_pretty(data).unwrap_or_default())
            }
            Part::File { .. } => println!("[file]"),
        }
    }
}

fn print_parts_inline(parts: &[Part]) {
    for part in parts {
        match part {
            Part::Text { text, .. } => print!("{text}"),
            Part::Data { data, .. } => {
                print!("{}", serde_json::to_string_pretty(data).unwrap_or_default())
            }
            Part::File { .. } => print!("[file]"),
        }
    }
    let _ = io::stdout().flush();
}

fn log_header(label: &str) {
    println!("\n{} {}", ">>".dimmed(), label.bold());
}
