using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalAtMostOneWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    internal sealed class WriteScope : IDisposable
    {
        public int SendCount;

        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = null;
            }
        }
    }

    internal sealed class DuplicateWriteSentinelException : Exception
    {
        public DuplicateWriteSentinelException()
            : base("A PayPal write was blocked from being resent.")
        {
        }
    }

    public static WriteScope Begin()
    {
        var scope = new WriteScope();
        Current.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var isTokenRequest = path.Contains("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase);
        var isWrite = !isTokenRequest
                      && (request.Method == HttpMethod.Post
                          || request.Method == HttpMethod.Put
                          || request.Method == HttpMethod.Patch
                          || request.Method == HttpMethod.Delete);

        if (isWrite && Current.Value is { } scope)
        {
            if (Interlocked.Increment(ref scope.SendCount) > 1)
            {
                throw new DuplicateWriteSentinelException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
