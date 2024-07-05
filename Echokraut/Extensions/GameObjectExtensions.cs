using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;

namespace Echokraut.Extensions;

public static class GameObjectExtensions
{
    public static uint? GetNpcId(this IGameObject gameObject)
    {
        if (gameObject is INpc npc)
        {
            return npc.DataId;
        }

        return null;
    }
}
