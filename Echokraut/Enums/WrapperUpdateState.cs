namespace Echokraut.Enums;

/// <summary>
/// What the Backend tab's wrapper button offers right now. The check costs a GitHub API request
/// (60/h per user IP when unauthenticated), so it only ever runs when the user asks for it — the
/// button therefore doubles as "check" and "update" instead of polling in the background.
/// </summary>
public enum WrapperUpdateState
{
    /// <summary>Nothing known yet — the button offers "Check for updates".</summary>
    NotChecked,

    /// <summary>Lookup in flight; the button is disabled.</summary>
    Checking,

    /// <summary>A newer release was found — the button offers "Update" and is clickable.</summary>
    UpdateAvailable,

    /// <summary>Checked, already on the newest release — "Update" stays visible but disabled, so the
    /// answer to "am I current?" is readable at a glance instead of the button vanishing.</summary>
    UpToDate,

    /// <summary>Lookup failed (offline, rate-limited, broken release). The button returns to
    /// "Check for updates" so it can be retried — a failed lookup must never read as "up to date".</summary>
    CheckFailed,
}
