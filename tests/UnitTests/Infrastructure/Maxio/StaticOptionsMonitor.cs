using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// An <see cref="IOptionsMonitor{TOptions}"/> over a fixed value, for code that reads options
/// through the monitor so a rotated secret takes effect without a restart.
/// </summary>
internal sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
{
    public StaticOptionsMonitor(TOptions value) => CurrentValue = value;

    public TOptions CurrentValue { get; }

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}
