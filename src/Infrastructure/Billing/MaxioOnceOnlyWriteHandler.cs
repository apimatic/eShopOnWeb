using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioWriteGuard
{
    private static readonly AsyncLocal<Scope?> Current = new();

    public static IDisposable Begin()
    {
        var scope = new Scope();
        Current.Value = scope;
        return scope;
    }

    public static bool TryAuthorizeSend()
    {
        var scope = Current.Value;
        if (scope is null)
        {
            return true;
        }

        if (scope.Sent)
        {
            return false;
        }

        scope.Sent = true;
        return true;
    }

    private sealed class Scope : IDisposable
    {
        public bool Sent { get; set; }

        public void Dispose()
        {
            if (Current.Value == this)
            {
                Current.Value = null;
            }
        }
    }
}

internal sealed class MaxioWriteAlreadySentException : Exception
{
    public MaxioWriteAlreadySentException()
        : base("A non-idempotent Maxio write was already sent for this operation.")
    {
    }
}

internal sealed class MaxioOnceOnlyWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isNonIdempotentWrite = request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete;

        if (isNonIdempotentWrite && !MaxioWriteGuard.TryAuthorizeSend())
        {
            throw new MaxioWriteAlreadySentException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
