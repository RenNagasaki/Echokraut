﻿using System;

namespace Echokraut.TextToTalk.TextProviders;

public class ComponentUpdateState<T> where T : struct, IEquatable<T>
{
    public Action<T> OnUpdate { get; set; }

    private T lastValue;

    public ComponentUpdateState()
    {
        OnUpdate = _ => { };
    }

    public void Mutate(T nextValue)
    {
        if (lastValue.Equals(nextValue))
        {
            return;
        }

        lastValue = nextValue;
        OnUpdate(nextValue);
    }
}
