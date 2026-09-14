using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Raised by <see cref="MaxioWriteOnceHandler"/> when it refuses to put a second copy of a
/// non-idempotent write on the wire.
/// <para>
/// Deliberately <em>not</em> an <see cref="System.Net.Http.HttpRequestException"/>: that is the exact
/// type the SDK's retry pipeline handles, so refusing with one would make the refusal itself retryable.
/// </para>
/// </summary>
public sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException()
        : base("A repeat send of a non-idempotent billing request was blocked.")
    {
    }
}

/// <summary>
/// Marks an ambient "at most one HTTP send may leave this scope" window.
/// <para>
/// The SDK's retry pipeline resends on <see cref="System.Net.Http.HttpRequestException"/> for
/// <em>every</em> verb — including the POST that creates a subscription — and a connection reset thrown
/// after the bytes reached the provider is indistinguishable from one thrown before. Since a duplicated
/// enrollment is real money and is externally visible, the only guarantee worth having is one that keeps
/// the send count at one; a blocked attempt never reaches the network.
/// </para>
/// <para>
/// The claim lives in an <see cref="AsyncLocal{T}"/> rather than on the <c>HttpRequestMessage</c>, because
/// the pipeline builds a fresh request object per attempt — a marker stored on the request is gone by the
/// retry and the guard never fires. Retries run inside the caller's async context, so this scope flows into
/// the handler on every attempt. The scope is disposed with the operation, so the claim is never left behind
/// to turn one transient failure into a permanent refusal.
/// </para>
/// </summary>
public sealed class SingleSendScope : IDisposable
{
    private static readonly AsyncLocal<SingleSendScope?> _current = new();

    private readonly SingleSendScope? _previous;
    private int _sends;

    public SingleSendScope()
    {
        _previous = _current.Value;
        _current.Value = this;
    }

    internal static SingleSendScope? Current => _current.Value;

    /// <summary>True once a send has been let through — i.e. the write may already have taken effect.</summary>
    public bool HasSent => Volatile.Read(ref _sends) > 0;

    /// <summary>
    /// Claims the single permitted send. Counted <em>before</em> the request goes out, because a request
    /// that failed on the way out may still have been received.
    /// </summary>
    internal bool TryClaimSend() => Interlocked.Increment(ref _sends) == 1;

    public void Dispose() => _current.Value = _previous;
}

/// <summary>
/// Signals that a write left this process but its outcome could not be established — the send failed on
/// the way out, or a resend was refused after one had already gone. Never treat it as "nothing happened":
/// the only way to settle it is to re-read provider state.
/// </summary>
public sealed class UnconfirmedWriteException : Exception
{
    public UnconfirmedWriteException(Exception cause)
        : base("A billing write was sent but its outcome could not be confirmed.", cause)
    {
    }
}
