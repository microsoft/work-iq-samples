use clap::{Parser, Subcommand};

/// WorkIQ endpoint configuration.
pub static WORKIQ_ENDPOINT: &str = "https://graph.microsoft.com/rp/workiq";
pub static WORKIQ_SCOPES: &[&str] = &["https://graph.microsoft.com/.default"];
pub static WORKIQ_AUTHORITY: &str = "https://login.microsoftonline.com/ca24a1b0-4df5-4b45-8126-22d617eb8f90";
pub static WORKIQ_EXTRA_HEADERS: &[(&str, &str)] = &[
    ("X-variants", "feature.EnableCopilotChatControllerEndpoint,feature.MSGraph3PCopilotToHelix,feature.EnableA2AServer"),
];

/// Work IQ A2A CLI — Interactive A2A session via WorkIQ
#[allow(clippy::doc_markdown)]
#[derive(Parser, Debug)]
#[command(version, about)]
pub struct Cli {
    #[command(subcommand)]
    pub command: Option<Command>,

    /// Auth token (JWT). Omit to use cached login or device code flow.
    #[arg(long, global = true)]
    pub token: Option<String>,

    /// Azure AD application (client) ID
    #[arg(long, global = true, env = "WORKIQ_APP_ID", default_value = "a668445b-6bb2-40f7-9aa6-87331e80db51")]
    pub appid: Option<String>,

    /// M365 account hint (e.g. user@contoso.com)
    #[arg(long, global = true)]
    pub account: Option<String>,

    /// Enable streaming mode (SSE)
    #[arg(long, global = true, default_value_t = false)]
    pub stream: bool,

    /// Verbosity level (0=quiet, 1=normal, 2=wire)
    #[arg(short, long, global = true, default_value_t = 1)]
    pub verbosity: u8,

    /// Show raw token in output
    #[arg(long, global = true, default_value_t = false)]
    pub show_token: bool,
}

#[derive(Subcommand, Debug)]
pub enum Command {
    /// Log in to M365 via device code flow and cache the token
    Login,
    /// Clear cached M365 tokens
    Logout,
    /// Show current auth status
    Status,
}
