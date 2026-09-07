using System;
using System.Collections.Concurrent;
using System.Numerics;
using Echokraut.DataClasses;

namespace Echokraut.Services;

/// <inheritdoc cref="IDialogStateService"/>
public class DialogStateService : IDialogStateService
{
    // Written from the speech pipeline (thread pool) and read from the addon hooks and the
    // toolbar (framework thread), so reads must not see a torn/stale reference.
    private volatile VoiceMessage? _currentVoiceMessage;
    private volatile bool _isVoiced;

    /// <summary>
    /// Concurrent because it is written from <c>ProcessSpeechAsync</c> (async, thread pool) and
    /// cleared from the addon lifecycle (framework thread). This used to be a plain
    /// <c>HashSet&lt;int&gt;</c> exposed as a public static field, which does not survive that
    /// overlap.
    /// </summary>
    private readonly ConcurrentDictionary<int, byte> _speakersResolvedThisDialog = new();

    public VoiceMessage? CurrentVoiceMessage
    {
        get => _currentVoiceMessage;
        set => _currentVoiceMessage = value;
    }

    public bool IsVoiced
    {
        get => _isVoiced;
        set => _isVoiced = value;
    }

    public Func<Vector2, bool>? IsInsideOwnedWindow { get; set; }

    public void MarkSpeakerResolved(int characterId)
        => _speakersResolvedThisDialog.TryAdd(characterId, 0);

    public bool WasSpeakerResolved(int characterId)
        => _speakersResolvedThisDialog.ContainsKey(characterId);

    public void ClearResolvedSpeakers()
        => _speakersResolvedThisDialog.Clear();
}
