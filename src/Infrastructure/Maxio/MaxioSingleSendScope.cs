using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Marks a region in which at most one HTTP request may leave for the billing provider.
/// </summary>
/// <remarks>
/// <para>
/// Creating a subscription is a non-idempotent write and the provider offers no idempotency key. The SDK's
/// retry pipeline resends on transport failures (a connection reset, a dropped socket) regardless of the
/// HTTP verb, and retries cannot be disabled — so without a guard a single enrollment call can enroll the
/// shopper more than once.
/// </para>
/// <para>
/// The claim is held here, in state that outlives the individual <c>HttpRequestMessage</c>: a new request
/// object is built for every attempt, so a marker stored on the request would be gone by the retry.
/// Retries run inside the caller's async context, so this <see cref="AsyncLocal{T}"/> flows into the
/// message handler on every attempt. The scope is released on dispose, so a blocked send never leaves a
/// stale claim behind.
/// </para>
/// </remarks>
public sealed class MaxioSingleSendScope : IDisposable
{
    private static readonly AsyncLocal<MaxioSingleSendScope?> CurrentScope = new();

    private readonly MaxioSingleSendScope? _parent;
    private int _sends;
    private bool _disposed;

    private MaxioSingleSendScope()
    {
        _parent = CurrentScope.Value;
        CurrentScope.Value = this;
    }

    internal static MaxioSingleSendScope? Current => CurrentScope.Value;

    /// <summary>Opens a scope in which only the first outbound request is allowed through.</summary>
    public static MaxioSingleSendScope Begin() => new();

    /// <summary>True once a request has been handed to the transport, whatever its outcome.</summary>
    public bool HasSent => Volatile.Read(ref _sends) > 0;

    /// <summary>
    /// Claims the single permitted send. Counted before the request goes out: a request that failed on
    /// the way out may still have been received, so the only safe reading is "this may have taken effect".
    /// </summary>
    internal bool TryClaimSend() => Interlocked.Increment(ref _sends) == 1;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CurrentScope.Value = _parent;
    }
}
