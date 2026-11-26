using System;
using System.Text.RegularExpressions;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// The log lines <c>tools/e2e/watch.sh</c> greps (plan › Log format):
/// <c>EKTEST &lt;id&gt; &lt;stage&gt; start|pass|fail|skip|progress &lt;detail&gt;</c> and
/// <c>EKTEST &lt;id&gt; done &lt;passed&gt;/&lt;failed&gt;/&lt;skipped&gt; &lt;reportPath&gt;</c>.
/// One line each, always through <see cref="Scrub"/>.
/// </summary>
public static class E2eLogFormat
{
    public const string Prefix = "EKTEST";
    public const string RemotePlaceholder = "<remote>";

    public static string Stage(string id, string stage, string kind, string detail, string? remoteUrl) =>
        OneLine($"{Prefix} {id} {stage} {kind} {Scrub(detail, remoteUrl)}".TrimEnd());

    public static string Outcome(string id, string stage, SelfTestOutcome outcome, string detail, string? remoteUrl) =>
        Stage(id, stage, Kind(outcome), detail, remoteUrl);

    public static string Done(string id, int passed, int failed, int skipped, string reportPath) =>
        OneLine($"{Prefix} {id} done {passed}/{failed}/{skipped} {reportPath}");

    public static string Kind(SelfTestOutcome outcome) => outcome switch
    {
        SelfTestOutcome.Pass => "pass",
        SelfTestOutcome.Fail => "fail",
        _ => "skip",
    };

    /// <summary>
    /// Replaces the remote URL and its host with <see cref="RemotePlaceholder"/>, case-insensitively —
    /// exception messages carry host names, and the private host never goes into a log or report.
    /// </summary>
    public static string Scrub(string text, string? remoteUrl)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(remoteUrl)) return text;
        text = ReplaceIgnoreCase(text, remoteUrl.TrimEnd('/'));
        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            text = ReplaceIgnoreCase(text, uri.Host);
        return text;
    }

    private static string ReplaceIgnoreCase(string text, string value) =>
        value.Length == 0 ? text : Regex.Replace(text, Regex.Escape(value), RemotePlaceholder, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ");
}
