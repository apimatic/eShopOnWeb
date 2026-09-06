using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// An ambient "exactly one send" window around a non-idempotent provider write.
/// </summary>
/// <remarks>
/// <para>
/// The SDK resilience pipeline resends a request on a transport failure regardless of the HTTP
/// verb, and that behaviour cannot be switched off. A connection reset thrown *after* the bytes
/// reached the provider is indistinguishable from one thrown before, so without a guard a single
/// shopper click can enroll that shopper twice.
/// </para>
/// <para>
/// The claim deliberately lives in an <see cref="AsyncLocal{T}"/> rather than on the
/// <see cref="System.Net.Http.HttpRequestMessage"/>: a fresh request object is built for every
/// attempt, so a marker stored on the request would already be gone by the retry. Retries run
/// inside the caller's async context, so this scope does flow into the message handler on every
/// attempt.
/// </para>
/// <para>
/// The claim is released when the scope is disposed. A scope wraps a single call and never
/// outlives it, so a failed write cannot leave a stale claim that blocks every later attempt.
/// </para>
/// </remarks>
internal static class MaxioWriteScope
{
    private static readonly AsyncLocal<Counter?> Current = new AsyncLocal<Counter?>();

    /// <summary>Opens a window in which exactly one outbound send is permitted.</summary>
    public static IDisposable Begin()
    {
        var previous = Current.Value;
        Current.Value = new Counter();
        return new Scope(previous);
    }

    /// <summary>
    /// Returns true when the send may proceed: always outside a scope, and only for the first send
    /// inside one. The send is counted *before* it goes out, because a request that failed on the
    /// way out may still have been received.
    /// </summary>
    public static bool TryClaimSend()
    {
        var counter = Current.Value;
        return counter is null || Interlocked.Increment(ref counter.Sends) == 1;
    }

    private sealed class Counter
    {
        public int Sends;
    }

    private sealed class Scope : IDisposable
    {
        private readonly Counter? _previous;

        public Scope(Counter? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            Current.Value = _previous;
        }
    }
}
