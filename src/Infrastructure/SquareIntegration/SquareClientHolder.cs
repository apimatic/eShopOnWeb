using System.Threading;
using Square;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Owns the long-lived <see cref="SquareClient"/> that acts for the merchant. The SDK caches the
/// access token per client, so when a merchant connects the client is replaced (<see cref="Reset"/>)
/// and the next call asks <see cref="SquareTokenSource"/> for the new merchant's token.
/// </summary>
public sealed class SquareClientHolder
{
    private readonly SquareClientFactory _factory;
    private readonly SquareTokenSource _tokenSource;
    private readonly object _gate = new();
    private SquareClient? _client;
    private int _generation;

    public SquareClientHolder(SquareClientFactory factory, SquareTokenSource tokenSource)
    {
        _factory = factory;
        _tokenSource = tokenSource;
    }

    public SquareClient Client
    {
        get
        {
            var client = Volatile.Read(ref _client);
            if (client is not null)
            {
                return client;
            }

            lock (_gate)
            {
                return _client ??= _factory.Create(_tokenSource);
            }
        }
    }

    /// <summary>Changes whenever the merchant connection changes; part of per-merchant cache keys.</summary>
    public int Generation => Volatile.Read(ref _generation);

    public void Reset()
    {
        lock (_gate)
        {
            _client = null;
            Interlocked.Increment(ref _generation);
        }
    }
}
