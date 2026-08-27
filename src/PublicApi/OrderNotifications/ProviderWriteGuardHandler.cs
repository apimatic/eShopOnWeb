using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

internal static class ProviderWriteScope
{
    private static readonly AsyncLocal<State?> Current = new();

    public static IDisposable Begin()
    {
        var prior = Current.Value;
        Current.Value = new State();
        return new Scope(prior);
    }

    public static bool TryClaimWrite()
    {
        var state = Current.Value;
        return state is null || Interlocked.Increment(ref state.Attempts) == 1;
    }

    private sealed class State
    {
        public int Attempts;
    }

    private sealed class Scope(State? prior) : IDisposable
    {
        public void Dispose() => Current.Value = prior;
    }
}

internal sealed class ProviderWriteGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head && !ProviderWriteScope.TryClaimWrite())
        {
            throw new DuplicateProviderWritePreventedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class DuplicateProviderWritePreventedException : Exception;
