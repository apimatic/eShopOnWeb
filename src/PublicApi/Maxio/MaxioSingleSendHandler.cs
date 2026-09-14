using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Refuses a re-send of a write within one logical SDK call.
///
/// The generated client retries transport failures (e.g. a dropped socket) on every verb,
/// including POST, so a subscription/customer create could otherwise reach Maxio more than
/// once. The service opens a <see cref="BeginWriteScope"/> around each guarded write; the
/// handler allows exactly one request to leave and throws <see cref="MaxioWriteResendException"/>
/// for any later attempt in the same logical call. Because the marker lives in an
/// <see cref="AsyncLocal{T}"/>, it flows into every retry attempt of the same call, and the
/// refusal is a non-retryable exception type so the pipeline cannot resend around it.
/// </summary>
internal sealed class MaxioSingleSendHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    /// <summary>Opens a scope that permits exactly one outbound request until disposed.</summary>
    public static IDisposable BeginWriteScope()
    {
        var scope = new WriteScope();
        Current.Value = scope;
        return scope;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = Current.Value;
        if (scope is not null && !scope.TryClaimSend())
        {
            throw new MaxioWriteResendException();
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private sealed class WriteScope : IDisposable
    {
        private int _claimed;

        public bool TryClaimSend() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;

        public void Dispose()
        {
            Current.Value = null;
        }
    }
}

/// <summary>
/// Sentinel thrown when a retry attempt is refused by <see cref="MaxioSingleSendHandler"/>.
/// Deliberately NOT an <see cref="HttpRequestException"/> so the SDK retry pipeline does not
/// retry it. The first (only) attempt may still have taken effect on the provider.
/// </summary>
internal sealed class MaxioWriteResendException : Exception
{
    public MaxioWriteResendException()
        : base("A Maxio write was refused because a previous attempt of the same request may already have been sent.")
    {
    }
}
