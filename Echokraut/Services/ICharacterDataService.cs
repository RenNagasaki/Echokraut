using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.DataClasses;
using Echokraut.Enums;

namespace Echokraut.Services;

public interface ICharacterDataService
{
    NpcRaces GetSpeakerRace(EKEventId eventId, IGameObject? speaker, out string raceStr, out int modelId);
    Genders GetCharacterGender(EKEventId eventId, IGameObject? speaker, NpcRaces race, out byte modelBody);
}
