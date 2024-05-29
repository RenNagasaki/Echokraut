using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;

namespace Echokraut.Extensions;

public static class GameObjectExtensions
{
    public static uint? GetNpcId(this GameObject gameObject)
    {
        if (gameObject is Npc npc)
        {
            return npc.DataId;
        }

        return null;
    }
}
