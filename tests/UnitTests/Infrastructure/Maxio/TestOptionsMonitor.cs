using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{TOptions}"/> over a single value, so tests can hand settings to
/// components that expect to observe configuration changes.
/// </summary>
public class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
