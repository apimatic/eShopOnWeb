using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Prevents an SDK transport retry from sending the same provider write twice.</summary>
public sealed class MaxioSingleSendHandler : DelegatingHandler
{
    private static readonly AsyncLocal<ConcurrentDictionary<string, byte>?> SentWrites = new();

    public static IDisposable BeginWriteScope()
    {
        var previous = SentWrites.Value;
        SentWrites.Value = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        return new Scope(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && SentWrites.Value is { } sentWrites)
        {
            var key = $"{request.Method}:{request.RequestUri}";
            if (!sentWrites.TryAdd(key, 0))
            {
                throw new MaxioWriteRetryBlockedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class Scope : IDisposable
    {
        private readonly ConcurrentDictionary<string, byte>? _previous;
        public Scope(ConcurrentDictionary<string, byte>? previous) => _previous = previous;
        public void Dispose() => SentWrites.Value = _previous;
    }
}

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException() : base("A Maxio write retry was blocked because its outcome is unknown.") { }
}
