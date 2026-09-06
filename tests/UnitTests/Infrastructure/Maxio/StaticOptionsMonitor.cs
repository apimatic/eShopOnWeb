using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// An <see cref="IOptionsMonitor{TOptions}"/> over a fixed value, so tests can construct services
/// that take one without standing up the options infrastructure.
/// </summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
