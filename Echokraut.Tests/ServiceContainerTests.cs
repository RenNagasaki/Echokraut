using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

public class ServiceContainerTests
{
    // ── Registration & resolution ───────────────────────────────────────────

    [Fact]
    public void GetService_Singleton_ReturnsSameInstance()
    {
        var container = new ServiceContainer();
        var instance = new ConcreteService();
        container.RegisterSingleton<ITestService>(instance);

        var result = container.GetService<ITestService>();

        Assert.Same(instance, result);
    }

    [Fact]
    public void GetService_CalledTwice_ReturnsSameInstance()
    {
        var container = new ServiceContainer();
        container.RegisterSingleton<ITestService>(new ConcreteService());

        var a = container.GetService<ITestService>();
        var b = container.GetService<ITestService>();

        Assert.Same(a, b);
    }

    [Fact]
    public void GetService_UnregisteredType_Throws()
    {
        var container = new ServiceContainer();

        Assert.Throws<InvalidOperationException>(() => container.GetService<ITestService>());
    }

    // ── Factory ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetService_Factory_NotCalledUntilFirstGet()
    {
        var container = new ServiceContainer();
        var callCount = 0;
        container.RegisterFactory<ITestService>(_ => { callCount++; return new ConcreteService(); });

        Assert.Equal(0, callCount);
        container.GetService<ITestService>();
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetService_Factory_ResultCached()
    {
        var container = new ServiceContainer();
        var callCount = 0;
        container.RegisterFactory<ITestService>(_ => { callCount++; return new ConcreteService(); });

        container.GetService<ITestService>();
        container.GetService<ITestService>();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetService_Factory_CanResolveDependencies()
    {
        var container = new ServiceContainer();
        container.RegisterSingleton<ITestService>(new ConcreteService());
        container.RegisterFactory<ITestService2>(c =>
            new ConcreteService2(c.GetService<ITestService>()));

        var svc2 = container.GetService<ITestService2>();
        Assert.NotNull(svc2.Inner);
    }

    // ── HasService ───────────────────────────────────────────────────────────

    [Fact]
    public void HasService_ReturnsTrueForRegisteredSingleton()
    {
        var container = new ServiceContainer();
        container.RegisterSingleton<ITestService>(new ConcreteService());

        Assert.True(container.HasService<ITestService>());
    }

    [Fact]
    public void HasService_ReturnsTrueForRegisteredFactory()
    {
        var container = new ServiceContainer();
        container.RegisterFactory<ITestService>(_ => new ConcreteService());

        Assert.True(container.HasService<ITestService>());
    }

    [Fact]
    public void HasService_ReturnsFalseForUnregistered()
    {
        var container = new ServiceContainer();

        Assert.False(container.HasService<ITestService>());
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CallsDisposeOnServices()
    {
        var container = new ServiceContainer();
        var disposable = new DisposableService();
        container.RegisterSingleton<ITestService>(disposable);

        container.Dispose();

        Assert.True(disposable.Disposed);
    }

    [Fact]
    public void Dispose_DoesNotThrowForNonDisposableServices()
    {
        var container = new ServiceContainer();
        container.RegisterSingleton<ITestService>(new ConcreteService());

        var ex = Record.Exception(() => container.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DisposesFactoryInstantiatedServices()
    {
        var container = new ServiceContainer();
        var disposable = new DisposableService();
        container.RegisterFactory<ITestService>(_ => disposable);
        container.GetService<ITestService>(); // instantiate it

        container.Dispose();

        Assert.True(disposable.Disposed);
    }

    [Fact]
    public void Dispose_ThrowingService_StillDisposesOthers()
    {
        // A throwing Dispose must not skip the remaining services — the DatabaseService (which
        // releases the SQLite file lock) must be disposed even if an earlier service throws.
        var container = new ServiceContainer();
        var good = new DisposableService();
        container.RegisterSingleton<ITestService>(new ThrowingDisposableService());
        container.RegisterSingleton<ITestService3>(good);

        var ex = Record.Exception(() => container.Dispose());

        Assert.Null(ex);          // exception is swallowed, not propagated
        Assert.True(good.Disposed); // the other service was still disposed
    }

    // ── Clear ────────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllRegistrations()
    {
        var container = new ServiceContainer();
        container.RegisterSingleton<ITestService>(new ConcreteService());
        container.Clear();

        Assert.False(container.HasService<ITestService>());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    interface ITestService { }
    interface ITestService2 { ITestService Inner { get; } }
    interface ITestService3 { }

    class ConcreteService : ITestService { }
    class DisposableService : ITestService, ITestService3, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    class ThrowingDisposableService : ITestService, IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("boom");
    }
    class ConcreteService2 : ITestService2
    {
        public ITestService Inner { get; }
        public ConcreteService2(ITestService inner) => Inner = inner;
    }
}
