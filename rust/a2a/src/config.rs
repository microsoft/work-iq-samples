use clap::{Parser, Subcommand};

// Work IQ Gateway — A2A endpoint multiplexed at /a2a/. Token audience is
// the Work IQ app ID; delegated scope `WorkIQAgent.Ask` is the only one needed.
pub const WORKIQ_ENDPOINT: &str = "https://workiq.svc.cloud.microsoft/a2a/";
pub const WORKIQ_SCOPES: &[&str] = &["api://workiq.svc.cloud.microsoft/.default"];
pub const WORKIQ_AUTHORITY: &str = "https://login.microsoftonline.com/common";

/// Work IQ A2A CLI — Interactive A2A session against the Work IQ Gateway
#[derive(Parser, Debug)]
#[command(version, about)]
pub struct Cli {
    #[command(subcommand)]
    pub command: Option<Command>,

    /// Auth token (JWT). Omit to use cached login or interactive sign-in.
    #[arg(long, global = true)]
    pub token: Option<String>,

    /// Azure AD application (client) ID
    #[arg(long, short = 'a', global = true, env = "WORKIQ_APP_ID")]
    pub appid: Option<String>,

    /// M365 account hint (e.g. user@contoso.com)
    #[arg(long, global = true)]
    pub account: Option<String>,

    /// Custom HTTP header in 'Key: Value' format (repeatable)
    #[arg(long = "header", short = 'H', global = true)]
    pub headers: Vec<String>,

    /// Verbosity level (0=quiet, 1=normal, 2=wire)
    #[arg(short, long, global = true, default_value_t = 1)]
    pub verbosity: u8,

    /// Show raw token in output
    #[arg(long, global = true)]
    pub show_token: bool,
}

#[derive(Subcommand, Debug)]
pub enum Command {
    /// Sign in interactively and cache the token
    Login,
    /// Clear cached tokens
    Logout,
    /// Show current auth status
    Status,
}
