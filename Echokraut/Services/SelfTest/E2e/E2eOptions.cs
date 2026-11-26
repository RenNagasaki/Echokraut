using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Echokraut.DataClasses;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Config options a stage changes for its duration (plan › Run state, <c>touchedOptions</c>). The first
/// <see cref="Set"/> of an option records its old value in <see cref="E2eState.TouchedOptions"/> (JSON,
/// survives a reload); <see cref="Restore"/> puts it back and drops the record. Only top-level
/// properties of <see cref="Configuration"/>, addressed by name (<c>nameof(Configuration.VoiceChat)</c>).
/// </summary>
public static class E2eOptions
{
    public static void Set<T>(E2eState state, Configuration config, string name, T value)
    {
        var prop = Property(name);
        if (!state.TouchedOptions.ContainsKey(name))
            state.TouchedOptions[name] = JsonSerializer.Serialize(prop.GetValue(config), prop.PropertyType);
        prop.SetValue(config, value);
    }

    /// <summary>Restores <paramref name="names"/> (null = every touched option); returns the restored names.</summary>
    public static IReadOnlyList<string> Restore(E2eState state, Configuration config, IEnumerable<string>? names = null)
    {
        var restored = new List<string>();
        foreach (var name in (names ?? state.TouchedOptions.Keys).ToList())
        {
            if (!state.TouchedOptions.TryGetValue(name, out var json)) continue;
            var prop = Property(name);
            prop.SetValue(config, JsonSerializer.Deserialize(json, prop.PropertyType));
            state.TouchedOptions.Remove(name);
            restored.Add(name);
        }
        return restored;
    }

    private static PropertyInfo Property(string name) =>
        typeof(Configuration).GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { CanRead: true, CanWrite: true } p
            ? p
            : throw new ArgumentException($"Configuration has no writable option '{name}'.", nameof(name));
}
