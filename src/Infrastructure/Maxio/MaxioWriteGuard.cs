using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Guards non-idempotent writes against the SDK's transport-level retry, which resends a POST on a
/// connection failure regardless of HTTP verb — potentially executing a create more than once. A
/// caller opens a write scope around a single create; the <see cref="SingleSendWriteGuardHandler"/>
/// allows the first send within that scope and refuses any resend by throwing
/// <see cref="MaxioWriteRetryBlockedException"/> (a private sentinel type — deliberately NOT an
/// <see cref="HttpRequestException"/>, so the retry pipeline does not re-trigger on it). The scope
/// is an <see cref="AsyncLocal{T}"/> so it flows into the handler across retry attempts, which run
/// in the caller's async context.
/// </summary>
internal static class MaxioWriteGuard
{
    private sealed class WriteState
    {
        public int Sends;
    }

    private static readonly AsyncLocal<WriteState?> _current = new();

    /// <summary>Opens a single-send scope; dispose to close it.</summary>
    public static IDisposable BeginScope()
    {
        _current.Value = new WriteState();
        return new Scope();
    }

    /// <summary>
    /// Records an outgoing send within the active scope and returns the send count. Returns 0 when
    /// no scope is active (reads are unguarded and retry normally).
    /// </summary>
    public static int RecordSend()
    {
        var state = _current.Value;
        return state is null ? 0 : Interlocked.Increment(ref state.Sends);
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => _current.Value = null;
    }
}

/// <summary>
/// Sentinel raised when a guarded write is re-sent within a single-send scope. It derives directly
/// from <see cref="Exception"/> so the SDK retry pipeline (which only retries
/// <see cref="HttpRequestException"/>) does not retry it.
/// </summary>
internal sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A billing write was re-sent by the transport retry and was blocked to keep the write single.")
    {
    }
}

/// <summary>
/// Delegating handler that enforces the single-send policy. Counts the send <em>before</em> it goes
/// out (a request that failed on the way out may still have been received), so a blocked resend is
/// surfaced as an unknown outcome to be reconciled, not a definite failure.
/// </summary>
internal sealed class SingleSendWriteGuardHandler : DelegatingHandler
{
    public SingleSendWriteGuardHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sendNumber = MaxioWriteGuard.RecordSend();
        if (sendNumber > 1)
        {
            throw new MaxioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
