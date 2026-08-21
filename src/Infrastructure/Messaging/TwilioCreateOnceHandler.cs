using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class DuplicateProviderWriteException : Exception
{
    public DuplicateProviderWriteException()
        : base("A duplicate provider write was blocked.")
    {
    }
}

internal sealed class TwilioCreateOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<SendScope?> Current = new();

    private sealed class SendScope
    {
        public int Sends;
    }

    public static IDisposable BeginCreateScope()
    {
        var scope = new SendScope();
        Current.Value = scope;
        return new Resetter();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post &&
            request.RequestUri is not null &&
            request.RequestUri.AbsolutePath.EndsWith("/Messages.json", StringComparison.OrdinalIgnoreCase))
        {
            var scope = Current.Value;
            if (scope is not null && Interlocked.Increment(ref scope.Sends) > 1)
            {
                throw new DuplicateProviderWriteException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class Resetter : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}
