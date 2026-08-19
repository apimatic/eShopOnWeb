using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Transport retries resend every verb. For POST/PATCH/DELETE this handler refuses a second send
/// so a duplicate write never reaches Maxio. The "already sent" marker lives in an AsyncLocal
/// scope, not on the HttpRequestMessage (a retry builds a new request object).
/// </summary>
internal sealed class WriteOnceDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsNonIdempotentWrite(request.Method))
        {
            var scope = WriteOnceScope.Current;
            if (scope is not null)
            {
                if (Interlocked.CompareExchange(ref scope.WritesSent, 1, 0) != 0)
                {
                    throw new DuplicateWriteRejectedException();
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsNonIdempotentWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete;
}

internal sealed class WriteOnceScope : IDisposable
{
    private static readonly AsyncLocal<WriteOnceScope?> Ambient = new();

    public int WritesSent;

    public static WriteOnceScope? Current => Ambient.Value;

    public static WriteOnceScope Begin()
    {
        var scope = new WriteOnceScope();
        Ambient.Value = scope;
        return scope;
    }

    public void Dispose()
    {
        if (ReferenceEquals(Ambient.Value, this))
        {
            Ambient.Value = null;
        }
    }
}

internal sealed class DuplicateWriteRejectedException : Exception
{
    public DuplicateWriteRejectedException()
        : base("A write was blocked because it may already have reached the billing provider.")
    {
    }
}
