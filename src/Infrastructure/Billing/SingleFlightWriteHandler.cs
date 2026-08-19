using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Blocks a second send of a non-GET write inside a <see cref="BeginWrite"/> scope.
/// Count lives in <see cref="AsyncLocal{T}"/> so it survives SDK retries (each retry is a new request).
/// </summary>
public sealed class SingleFlightWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteGuard?> Guard = new();

    public static IDisposable BeginWrite()
    {
        var scope = new WriteGuard();
        Guard.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var guard = Guard.Value;
        if (guard is not null && IsWrite(request.Method))
        {
            if (Interlocked.Exchange(ref guard.Sent, 1) == 1)
            {
                throw new DuplicateWritePreventedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Put || method == HttpMethod.Delete;

    private sealed class WriteGuard : IDisposable
    {
        public int Sent;
        public void Dispose()
        {
            if (ReferenceEquals(Guard.Value, this))
            {
                Guard.Value = null;
            }
        }
    }
}
