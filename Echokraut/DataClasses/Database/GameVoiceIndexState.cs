namespace Echokraut.DataClasses.Database;

/// <summary>Summary of one indexed (token, epoch) — see <c>IDatabaseService.GetGameVoiceIndexStates</c>.</summary>
public sealed record GameVoiceIndexState(string Token, string Epoch, int IndexVersion, string LinesHash);
