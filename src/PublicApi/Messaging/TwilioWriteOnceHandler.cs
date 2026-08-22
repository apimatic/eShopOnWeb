using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteOnceState?> Current = new();

    internal static IDisposable BeginWrite()
    {
        var state = new WriteOnceState();
        Current.Value = state;
        return state;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && Current.Value is { } state)
        {
            if (Interlocked.Increment(ref state.SendCount) > 1)
                throw new TwilioDuplicateWritePreventedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteOnceState : IDisposable
    {
        public int SendCount;

        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, this))
                Current.Value = null;
        }
    }
}
