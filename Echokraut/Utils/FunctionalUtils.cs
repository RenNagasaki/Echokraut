using Dalamud.Plugin.Services;
using System;
using System.Linq;

namespace Echokraut.Utils;

public static class FunctionalUtils
{
    public static void RunSafely(Action fn, Action<Exception> onFail, IPluginLog log)
    {
        try
        {
            log.Info("HELLO IM HERE");
            fn();
        }
        catch (Exception e)
        {
            onFail(e);
        }
    }

    public static T Pipe<T>(T input, params Func<T, T>[] transforms)
    {
        return transforms.Aggregate(input, (agg, next) => next(agg));
    }
}
