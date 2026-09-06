using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown by <see cref="MaxioSingleSendHandler"/> when it refuses to re-send a write.
/// </summary>
/// <remarks>
/// Deliberately derives from <see cref="Exception"/> and <em>not</em> from
/// <see cref="HttpRequestException"/>: the SDK's retry pipeline retries <c>HttpRequestException</c> on
/// every verb, so refusing with one would make the refusal itself retryable.
/// </remarks>
internal sealed class MaxioDuplicateSendException : Exception
{
    public MaxioDuplicateSendException(string message) : base(message)
    {
    }
}

/// <summary>
/// Holds an "at most one send" claim across the SDK's retry pipeline.
/// </summary>
/// <remarks>
/// The SDK retries a transport failure (connection reset, dropped socket) on <em>any</em> verb, and that
/// behaviour cannot be switched off — <c>MaxRetries</c> has a floor of 1. A reset thrown after the bytes
/// reached Maxio is indistinguishable from one thrown before, so an unguarded retry can enroll a customer
/// twice. The claim is kept in an <see cref="AsyncLocal{T}"/> rather than on the
/// <see cref="HttpRequestMessage"/>, because the pipeline builds a fresh request object per attempt and a
/// per-request marker would be gone by the retry; retries run inside the caller's async context, so the
/// scope flows into the handler on every attempt.
/// </remarks>
internal static class MaxioSingleSendGuard
{
    private static readonly AsyncLocal<Claim?> CurrentClaim = new();

    /// <summary>
    /// Opens a scope in which at most one request may leave the process. Dispose it before making any
    /// follow-up (reconciliation) call, so those reads are not counted against the claim.
    /// </summary>
    public static IDisposable BeginSingleSend(string description) => new Scope(description);

    /// <summary>
    /// Counts a send against the active claim. Returns false when the send must be refused.
    /// The send is counted <em>before</em> it goes out: a request that fails on the way out may still have
    /// been received, so the only safe reading is "this may already have taken effect".
    /// </summary>
    public static bool TryClaimSend(out string description)
    {
        var claim = CurrentClaim.Value;
        if (claim is null)
        {
            description = string.Empty;
            return true;
        }

        description = claim.Description;
        return Interlocked.Increment(ref claim.Sends) == 1;
    }

    private sealed class Claim
    {
        public Claim(string description) => Description = description;

        public string Description { get; }

        public int Sends;
    }

    private sealed class Scope : IDisposable
    {
        private readonly Claim? _previous;

        public Scope(string description)
        {
            _previous = CurrentClaim.Value;
            CurrentClaim.Value = new Claim(description);
        }

        public void Dispose() => CurrentClaim.Value = _previous;
    }
}

/// <summary>
/// Refuses any re-send the SDK's retry pipeline attempts inside a
/// <see cref="MaxioSingleSendGuard.BeginSingleSend"/> scope. A blocked attempt never reaches the network,
/// which is the only way to actually hold the send count at one.
/// </summary>
internal sealed class MaxioSingleSendHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!MaxioSingleSendGuard.TryClaimSend(out var description))
        {
            return Task.FromException<HttpResponseMessage>(new MaxioDuplicateSendException(
                $"Refused to re-send '{description}' to Maxio: the first attempt may already have been applied."));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
