using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The SDK retries transport failures on every verb, including the non-idempotent
/// message-create POST, and a reset after the bytes reached the provider is
/// indistinguishable from one before — so a retried send can text the shopper twice.
/// This handler holds a send to at most one attempt: the messaging service opens a scope
/// around each send, and any retry of that send is refused before it reaches the network.
/// The refusal uses a private sentinel type (not HttpRequestException, which is retryable).
/// </summary>
public sealed class SendOnceGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<SendScope?> _currentScope = new();

    public static IDisposable BeginScope()
    {
        var scope = new SendScope();
        _currentScope.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = _currentScope.Value;
        if (scope != null && !scope.TryClaim())
        {
            throw new DuplicateSendPreventedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class SendScope : IDisposable
    {
        private int _claimed;

        public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;

        public void Dispose() => _currentScope.Value = null;
    }
}

/// <summary>Thrown when a send retry is refused. Derives from Exception directly so the retry pipeline does not retry the refusal.</summary>
public sealed class DuplicateSendPreventedException : Exception
{
    public DuplicateSendPreventedException()
        : base("A previous attempt of this send may already have reached the provider; the retry was refused to avoid a duplicate message.")
    {
    }
}
