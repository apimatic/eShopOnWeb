using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Refuses any re-send inside an open <see cref="SingleSendScope"/>, so a write cannot reach Maxio
/// more than once however the transport fails.
/// </summary>
internal sealed class SingleSendGuardHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!SingleSendScope.TryRegisterSend())
        {
            // Deliberately NOT an HttpRequestException: that is the very type the SDK's retry
            // pipeline re-sends on, so refusing with one would make the refusal itself retryable.
            throw new DuplicateSendRefusedException(request.Method.Method, request.RequestUri?.AbsolutePath);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Thrown when a re-send was blocked. The outcome of the one send that was allowed is unknown — it
/// may well have taken effect — so callers must settle it by re-reading provider state, never by
/// assuming the write did not happen.
/// </summary>
internal sealed class DuplicateSendRefusedException : Exception
{
    public DuplicateSendRefusedException(string method, string? path)
        : base($"Refused a retry of {method} {path}: the original request may already have been applied.")
    {
    }
}
