using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class SingleAttemptHandler : DelegatingHandler
{
    private static readonly AsyncLocal<AttemptScope?> CurrentScope = new();

    public static IDisposable BeginWrite()
    {
        if (CurrentScope.Value is not null)
        {
            throw new InvalidOperationException("A Twilio write-attempt scope is already active.");
        }

        CurrentScope.Value = new AttemptScope();
        return new ScopeLease();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method != HttpMethod.Get && Interlocked.Increment(ref scope.Attempts) > 1)
        {
            throw new DuplicateProviderWriteBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class AttemptScope
    {
        public int Attempts;
    }

    private sealed class ScopeLease : IDisposable
    {
        public void Dispose() => CurrentScope.Value = null;
    }
}

public sealed class DuplicateProviderWriteBlockedException : Exception
{
    public DuplicateProviderWriteBlockedException()
        : base("A retry of a provider write was blocked because its outcome is unknown.") { }
}
