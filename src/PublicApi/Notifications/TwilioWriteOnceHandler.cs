using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioDuplicateWriteBlockedException : Exception
{
    public TwilioDuplicateWriteBlockedException()
        : base("A repeated provider write was blocked because the first attempt has an unknown outcome.")
    {
    }
}

public sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    private sealed class ScopeState
    {
        public int WriteCount;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly ScopeState? _prior;

        public ScopeLease(ScopeState? prior)
        {
            _prior = prior;
        }

        public void Dispose()
        {
            Current.Value = _prior;
        }
    }

    private static readonly AsyncLocal<ScopeState?> Current = new();

    public static IDisposable BeginScope()
    {
        var prior = Current.Value;
        Current.Value = new ScopeState();
        return new ScopeLease(prior);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = Current.Value;
        if (state is not null && request.Method == HttpMethod.Post && Interlocked.Increment(ref state.WriteCount) > 1)
        {
            throw new TwilioDuplicateWriteBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
