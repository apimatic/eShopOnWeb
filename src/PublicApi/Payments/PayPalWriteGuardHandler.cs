using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalWriteGuardHandler : DelegatingHandler
{
    private sealed class ScopeState { public int Sends; }
    private static readonly AsyncLocal<ScopeState?> Current = new();

    public static IDisposable BeginScope()
    {
        var prior = Current.Value;
        Current.Value = new ScopeState();
        return new Scope(() => Current.Value = prior);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var state = Current.Value;
        var isProviderWrite = request.Method != HttpMethod.Get && request.Method != HttpMethod.Head &&
            request.RequestUri?.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase) != true;
        if (state is not null && isProviderWrite && Interlocked.Increment(ref state.Sends) > 1)
            throw new DuplicateProviderSendBlockedException();
        return base.SendAsync(request, cancellationToken);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

public sealed class DuplicateProviderSendBlockedException : Exception
{
    public DuplicateProviderSendBlockedException()
        : base("A provider write may already have taken effect; reconcile before retrying.") { }
}
