using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Refuses an SDK transport-retry re-send of a write (POST) that the integration did not
/// authorise. The SDK retries <see cref="HttpRequestException"/> on every verb regardless of
/// the configured retry methods, so a POST (e.g. create-subscription) can reach the provider
/// more than once for a single logical call. The claim lives in an <see cref="AsyncLocal{T}"/>
/// scope (retries run inside the caller's async context) rather than on the request message,
/// because a fresh request object is built per attempt. The refusal throws a private sentinel
/// type — never an <see cref="HttpRequestException"/>, which would itself be retried.
/// </summary>
public sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteClaim?> CurrentClaim = new();

    public static IDisposable BeginWriteScope()
    {
        var claim = new WriteClaim();
        var previous = CurrentClaim.Value;
        CurrentClaim.Value = claim;
        return new ClaimScope(claim, previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (CurrentClaim.Value is { } claim &&
            string.Equals(request.Method?.Method, HttpMethod.Post.Method, StringComparison.Ordinal) &&
            claim.ConsumeSendAuthorization() == false)
        {
            // The first authorised send already went out (and may or may not have been
            // received); a re-send is not allowed. Outcome of the original attempt is
            // "unknown" and must be settled by re-reading provider state.
            throw new MaxioResendBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteClaim
    {
        private int _sends;

        public bool ConsumeSendAuthorization() => Interlocked.Increment(ref _sends) == 1;
    }

    private sealed class ClaimScope : IDisposable
    {
        private readonly WriteClaim _claim;
        private readonly WriteClaim? _previous;

        public ClaimScope(WriteClaim claim, WriteClaim? previous)
        {
            _claim = claim;
            _previous = previous;
        }

        public void Dispose() => CurrentClaim.Value = _previous;
    }
}

/// <summary>
/// Sentinel thrown by <see cref="MaxioWriteGuardHandler"/>. Deliberately NOT an
/// HttpRequestException so the SDK retry pipeline does not treat the refusal as retryable.
/// </summary>
public sealed class MaxioResendBlockedException : Exception
{
    public MaxioResendBlockedException()
        : base("A re-send of a Maxio write was blocked by the integration's write guard.")
    {
    }
}
