using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScopeState?> CurrentScope = new();

    public static IDisposable BeginScope()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new WriteScopeState();
        return new ScopeLease(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = CurrentScope.Value;
        if (state is not null && request.Method == HttpMethod.Post && Interlocked.Increment(ref state.Attempts) > 1)
        {
            throw new TwilioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScopeState
    {
        public int Attempts;
    }

    private sealed class ScopeLease(WriteScopeState? previous) : IDisposable
    {
        public void Dispose() => CurrentScope.Value = previous;
    }
}

internal sealed class TwilioWriteRetryBlockedException : Exception
{
    public TwilioWriteRetryBlockedException()
        : base("A retry of a provider write was blocked because its outcome is unknown.")
    {
    }
}
