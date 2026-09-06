using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Refuses to put a second copy of a guarded write on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's retry pipeline resends on <see cref="HttpRequestException"/> regardless of HTTP method,
/// and that trigger cannot be switched off. A connection reset thrown after the bytes reached Maxio is
/// indistinguishable from one thrown before, so an unguarded <c>POST /subscriptions.json</c> can enrol
/// the same customer twice. That is exactly the duplicate the hero flow must not produce.
/// </para>
/// <para>
/// Two details make this work. The claim lives in an <see cref="AsyncLocal{T}"/> scope owned by the
/// caller rather than on the <see cref="HttpRequestMessage"/>, because the pipeline builds a fresh
/// request object per attempt and any marker attached to one is gone by the retry. And the refusal is a
/// plain <see cref="Exception"/>, not an <see cref="HttpRequestException"/> — throwing the very type the
/// pipeline retries would make the refusal itself retryable.
/// </para>
/// <para>
/// The claim is scoped to a single <c>using</c>, so it is released as soon as the write completes and
/// can never become a stale claim that refuses every later attempt.
/// </para>
/// </remarks>
public sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Counted before the send: a request that failed on its way out may still have been received,
        // so "this may already have taken effect" is the only safe reading.
        if (!MaxioWriteScope.TryClaimSend())
        {
            throw new MaxioDuplicateSendBlockedException(request.Method, request.RequestUri);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Marks a region in which at most one HTTP request may be sent. Open one around every non-idempotent
/// Maxio call; calls made outside a scope are unguarded and send freely.
/// </summary>
public sealed class MaxioWriteScope : IDisposable
{
    private static readonly AsyncLocal<Claim?> CurrentClaim = new();

    private readonly Claim? _previous;
    private readonly Claim _claim;
    private bool _disposed;

    public MaxioWriteScope()
    {
        _previous = CurrentClaim.Value;
        _claim = new Claim();
        CurrentClaim.Value = _claim;
    }

    /// <summary>
    /// True when the scope's single send was used — i.e. the request really did leave this process.
    /// A failure after this point has an unknown outcome and must be settled by re-reading state.
    /// Read from the captured claim rather than the ambient one, so it stays correct after disposal.
    /// </summary>
    public bool WasSent => Volatile.Read(ref _claim.Sends) > 0;

    internal static bool TryClaimSend()
    {
        var claim = CurrentClaim.Value;

        // No scope open: an ordinary read, which is safe to resend.
        return claim is null || Interlocked.Increment(ref claim.Sends) == 1;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CurrentClaim.Value = _previous;
    }

    private sealed class Claim
    {
        public int Sends;
    }
}

/// <summary>
/// Thrown by <see cref="MaxioWriteGuardHandler"/> when the retry pipeline tries to resend a guarded
/// write. Deliberately not an <see cref="HttpRequestException"/>, so the refusal is not itself retried.
/// </summary>
public sealed class MaxioDuplicateSendBlockedException : Exception
{
    public MaxioDuplicateSendBlockedException(HttpMethod method, Uri? requestUri)
        : base($"Blocked a retry of {method} {requestUri?.AbsolutePath} to avoid sending the write twice.")
    {
    }
}
