using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Enforces write-once semantics for a scope that asks for it, and records the status the wire actually
/// returned.
/// </summary>
/// <remarks>
/// The SDK retries an <see cref="HttpRequestException"/> on <em>every</em> verb regardless of the configured
/// retryable methods, and a connection reset thrown after the bytes reached Maxio is indistinguishable from
/// one thrown before. Without this handler, a single dropped socket during a subscribe could enroll the
/// shopper more than once. A blocked attempt never reaches the network, which is the only way to hold the
/// send count at one.
/// </remarks>
internal sealed class MaxioCallScopeHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = MaxioCallScope.Current;

        if (scope is { SingleSend: true } && !scope.TryAuthorizeSend())
        {
            throw new MaxioDuplicateSendBlockedException(request.Method.Method, request.RequestUri?.AbsolutePath);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        scope?.RecordStatus((int)response.StatusCode);
        return response;
    }
}
