using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using ManagedBass;

namespace Echokraut.Services;

/// <summary>
/// Positions of the speakers of the 3D lines that are playing, read on the framework thread and
/// handed to the BASS position pollers, which run on timer threads.
/// Dalamud throws "Not on main thread!" for <see cref="IGameObject.Position"/> anywhere else, and
/// inside a <see cref="System.Threading.Timer"/> callback that exception took the whole game down
/// (crash 2026-11-21 on a chat line with 3D audio).
/// </summary>
internal sealed class SpeakerPositionCache
{
    // Vector3D is a class: entries are replaced, never mutated, so a reader never sees a torn value.
    private readonly ConcurrentDictionary<Guid, Vector3D> _positions = new();

    /// <summary>Framework thread only. Stores every valid speaker and drops streams that stopped playing.</summary>
    public void Refresh(IEnumerable<KeyValuePair<Guid, IGameObject?>> playing)
    {
        var alive = new HashSet<Guid>();
        foreach (var (id, speaker) in playing)
        {
            alive.Add(id);
            if (speaker == null || !speaker.IsValid()) continue;
            var p = speaker.Position;
            _positions[id] = new Vector3D(p.X, p.Y, p.Z);
        }
        foreach (var id in _positions.Keys)
            if (!alive.Contains(id)) _positions.TryRemove(id, out _);
    }

    /// <summary>Any thread. Null until the framework thread has seen the speaker once.</summary>
    public Vector3D? Get(Guid id) => _positions.TryGetValue(id, out var p) ? p : null;
}
