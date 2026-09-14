using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Holds a non-idempotent write to <em>at most one</em> outbound HTTP send.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's retry pipeline resends a request on a transport failure (a connection reset, a dropped socket)
/// regardless of the HTTP verb, and a reset thrown after the bytes reached Maxio is indistinguishable from
/// one thrown before. Creating a subscription is externally visible and costly to undo, so a resend is not
/// acceptable: a duplicate would be a second recurring charge.
/// </para>
/// <para>
/// The count is kept in an <see cref="AsyncLocal{T}"/> scope owned by the caller rather than on the
/// <c>HttpRequestMessage</c>, because the pipeline builds a fresh request object per attempt — a marker on
/// the request would be gone by the retry and the guard would never fire. Retries run inside the caller's
/// async context, so the scope flows into <see cref="MaxioWriteOnceHandler"/> on every attempt.
/// </para>
/// <para>
/// A blocked resend is reported with <see cref="MaxioDuplicateSendBlockedException"/>, which deliberately
/// does <em>not</em> derive from <see cref="System.Net.Http.HttpRequestException"/> — that is the very type
/// the pipeline retries, so refusing with one would make the refusal itself retryable.
/// </para>
/// </remarks>
public static class MaxioWriteGuard
{
    private static readonly AsyncLocal<StrongBox?> Current = new();

    /// <summary>
    /// Opens a scope in which exactly one outbound send is authorised. Dispose it once the outcome of that
    /// send has been settled — the scope is bounded by the <c>using</c> block, so a transient failure can
    /// never leave a permanent refusal behind.
    /// </summary>
    public static IDisposable BeginSingleSend()
    {
        var previous = Current.Value;
        Current.Value = new StrongBox();
        return new Scope(previous);
    }

    /// <summary>
    /// Called by <see cref="MaxioWriteOnceHandler"/> before each send. Returns false when the send is a
    /// resend inside an open single-send scope.
    /// </summary>
    internal static bool TryAuthorizeSend()
    {
        var box = Current.Value;
        if (box is null)
        {
            // No scope open: reads and other calls are unaffected by this guard.
            return true;
        }

        return Interlocked.Increment(ref box.Sends) == 1;
    }

    private sealed class StrongBox
    {
        public int Sends;
    }

    private sealed class Scope : IDisposable
    {
        private readonly StrongBox? _previous;
        private bool _disposed;

        public Scope(StrongBox? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = _previous;
        }
    }
}
