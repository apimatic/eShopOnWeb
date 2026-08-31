using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Guards a non-idempotent provider write so that, within one write scope, only ONE request actually
/// reaches the network — even though the SDK's Polly pipeline retries transport failures
/// (<see cref="HttpRequestException"/>) on every verb, including POST. Without this a connection reset
/// after a create/issue was sent could raise a second, duplicate bill.
///
/// The claim count is kept in an <see cref="AsyncLocal{T}"/> scope opened by the caller around the write,
/// not on the <see cref="HttpRequestMessage"/> (which is rebuilt per retry attempt), so it survives across
/// retries which run inside the caller's async context.
/// </summary>
internal static class SingleSendGuard
{
    private sealed class SendCounter
    {
        public int Sent;
    }

    private static readonly AsyncLocal<SendCounter?> Scope = new();

    /// <summary>Open a write scope. Dispose it once the write's outcome is settled.</summary>
    public static IDisposable BeginScope()
    {
        Scope.Value = new SendCounter();
        return new ScopeReleaser();
    }

    /// <summary>
    /// Claim the single permitted send for the current scope. Returns true for the first attempt only;
    /// outside any scope it always returns true (nothing to guard).
    /// </summary>
    public static bool TryClaimSend()
    {
        var counter = Scope.Value;
        if (counter is null)
        {
            return true;
        }

        return Interlocked.Increment(ref counter.Sent) == 1;
    }

    private sealed class ScopeReleaser : IDisposable
    {
        public void Dispose() => Scope.Value = null;
    }
}

/// <summary>
/// A refusal that a re-send was blocked. Deliberately NOT an <see cref="HttpRequestException"/> — that is
/// the type the retry pipeline retries — so the refusal itself is not retried and propagates out to the
/// integration boundary, which translates it into an "outcome unknown" provider error.
/// </summary>
internal sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException()
        : base("A provider write was interrupted after being sent and was not re-sent, to avoid a duplicate bill.")
    {
    }
}

/// <summary>
/// The delegating handler that enforces <see cref="SingleSendGuard"/> for POST writes.
/// </summary>
internal sealed class SingleSendGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !SingleSendGuard.TryClaimSend())
        {
            throw new DuplicateSendBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
