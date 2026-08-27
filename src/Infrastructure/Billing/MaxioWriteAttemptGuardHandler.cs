using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioWriteAttemptGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        MaxioWriteAttemptScope.Record(request.Method);
        return base.SendAsync(request, cancellationToken);
    }
}

internal static class MaxioWriteAttemptScope
{
    private static readonly AsyncLocal<AttemptState?> Current = new();

    public static IDisposable Begin()
    {
        var prior = Current.Value;
        Current.Value = new AttemptState();
        return new Scope(prior);
    }

    public static void Record(HttpMethod method)
    {
        var state = Current.Value;
        if (state is null || method != HttpMethod.Post)
        {
            return;
        }

        if (Interlocked.Increment(ref state.Writes) > 1)
        {
            throw new MaxioRepeatedWriteBlockedException();
        }
    }

    private sealed class AttemptState
    {
        public int Writes;
    }

    private sealed class Scope : IDisposable
    {
        private readonly AttemptState? _prior;

        public Scope(AttemptState? prior)
        {
            _prior = prior;
        }

        public void Dispose()
        {
            Current.Value = _prior;
        }
    }
}

internal sealed class MaxioRepeatedWriteBlockedException : Exception
{
}
