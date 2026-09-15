using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException()
        : base("A duplicate messaging write was blocked before it reached the provider.")
    {
    }
}

internal static class TwilioWriteGuard
{
    private static readonly AsyncLocal<int> OutboundWrites = new();

    public static IDisposable BeginScope()
    {
        OutboundWrites.Value = 0;
        return new Reset();
    }

    public static void CountOutboundWrite()
    {
        if (OutboundWrites.Value >= 1)
        {
            throw new TwilioDuplicateWriteException();
        }

        OutboundWrites.Value++;
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => OutboundWrites.Value = 0;
    }
}

internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsMessageWrite(request))
        {
            TwilioWriteGuard.CountOutboundWrite();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsMessageWrite(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post && request.Method != HttpMethod.Delete)
        {
            return false;
        }

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        return path.Contains("/Messages", StringComparison.OrdinalIgnoreCase);
    }
}
