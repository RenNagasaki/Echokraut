using System;
using System.Collections.Generic;

namespace Echokraut.Services;

/// <summary>
/// Simple dependency injection container for managing plugin services
/// </summary>
public class ServiceContainer : IDisposable
{
    private readonly Dictionary<Type, object> _services = new();
    private readonly Dictionary<Type, Func<ServiceContainer, object>> _factories = new();

    /// <summary>
    /// Register a singleton service instance
    /// </summary>
    public void RegisterSingleton<TInterface>(TInterface instance) where TInterface : class
    {
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        _services[typeof(TInterface)] = instance;
    }

    /// <summary>
    /// Register a service factory for lazy initialization
    /// </summary>
    public void RegisterFactory<TInterface>(Func<ServiceContainer, TInterface> factory) where TInterface : class
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        _factories[typeof(TInterface)] = c => factory(c)!;
    }

    /// <summary>
    /// Get a service instance, creating it if necessary
    /// </summary>
    public TInterface GetService<TInterface>() where TInterface : class
    {
        var type = typeof(TInterface);
        
        // Check if already instantiated
        if (_services.TryGetValue(type, out var service))
        {
            return (TInterface)service;
        }

        // Check if we have a factory
        if (_factories.TryGetValue(type, out var factory))
        {
            var instance = (TInterface)factory(this);
            _services[type] = instance;
            return instance;
        }

        throw new InvalidOperationException($"Service {typeof(TInterface).Name} is not registered");
    }

    /// <summary>
    /// Check if a service is registered
    /// </summary>
    public bool HasService<TInterface>() where TInterface : class
    {
        var type = typeof(TInterface);
        return _services.ContainsKey(type) || _factories.ContainsKey(type);
    }

    /// <summary>
    /// Clear all services
    /// </summary>
    public void Clear()
    {
        _services.Clear();
        _factories.Clear();
    }

    public void Dispose()
    {
        foreach (var service in _services.Values)
        {
            if (service is IDisposable disposable)
                disposable.Dispose();
        }
        _services.Clear();
        _factories.Clear();
    }
}
