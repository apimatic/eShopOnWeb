using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioWriteOnceGate
{
    private static readonly AsyncLocal<int> Attempts = new();

    public static IDisposable Begin()
    {
        Attempts.Value = 0;
        return new Resetter();
    }

    public static void CountOutboundWrite()
    {
        var next = Attempts.Value + 1;
        Attempts.Value = next;
        if (next > 1)
            throw new TwilioWriteRetryRefusedException();
    }

    private sealed class Resetter : IDisposable
    {
        public void Dispose() => Attempts.Value = 0;
    }
}

internal sealed class TwilioWriteRetryRefusedException : Exception
{
    public TwilioWriteRetryRefusedException()
        : base("A retried messaging write was refused.")
    {
    }
}

internal sealed class TwilioSingleWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get
            && request.Method != HttpMethod.Head
            && request.Method != HttpMethod.Options)
        {
            TwilioWriteOnceGate.CountOutboundWrite();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
