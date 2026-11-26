using System;
using System.Linq;
using System.Net.Sockets;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Tests that talk to real remote services (GitHub, Google Drive, Lodestone, …). They check remote
/// state, not Echokraut code, so the default run (<c>node .claude/verify.js</c>, CI) leaves them out
/// with <c>--filter "Category!=Live"</c>. Mark the class with <c>[Trait("Category", Live.Category)]</c>
/// and each test with <see cref="LiveFactAttribute"/>: run explicitly, a test without network or
/// without its required environment variables is reported as skipped instead of passing silently.
/// Run them with <c>dotnet test --filter "Category=Live"</c>.
/// </summary>
internal static class Live
{
    public const string Category = "Live";

    private const string ProbeHost = "github.com";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private static readonly Lazy<bool> Online = new(ProbeOnline);

    /// <summary>Null when the test can run, otherwise the skip reason.</summary>
    public static string? SkipReason(string[] requiredEnvVars)
    {
        if (requiredEnvVars.Length > 0)
        {
            TestEnvLoader.EnsureLoaded();
            var missing = requiredEnvVars
                .Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)))
                .ToArray();
            if (missing.Length > 0)
                return $"Live test: set {string.Join(" + ", missing)} (shell or repo-root .env, see .example.env).";
        }

        return Online.Value ? null : $"Live test: no network ({ProbeHost}:443 unreachable).";
    }

    private static bool ProbeOnline()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(ProbeHost, 443).Wait(ProbeTimeout) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// <see cref="FactAttribute"/> for a live test: skipped without network or without the given
/// environment variables. See <see cref="Live"/>.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(params string[] requiredEnvVars) => Skip = Live.SkipReason(requiredEnvVars);
}
