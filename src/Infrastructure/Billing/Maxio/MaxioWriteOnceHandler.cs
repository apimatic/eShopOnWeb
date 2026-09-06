using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Holds Maxio writes to at most one send.
/// <para>
/// The SDK retry pipeline cannot be switched off, and its transport trigger is a bare
/// <c>Handle&lt;HttpRequestException&gt;</c> that ignores the idempotent-method list. So a connection reset
/// on a <c>POST</c> is re-sent even though the write may already have reached Maxio, which would enroll a
/// shopper twice. Refusing the resend inside the pipeline is the only place a duplicate can be stopped
/// before it reaches the network.
/// </para>
/// <para>
/// Reads are untouched, and so are writes made outside a <see cref="MaxioWriteScope"/>.
/// </para>
/// </summary>
internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var scope = MaxioWriteScope.Current;

        if (scope is null || !IsWrite(request.Method))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (!scope.TryClaimSend())
        {
            throw new MaxioWriteResendBlockedException(scope.Operation);
        }

        var response = await base.SendAsync(request, cancellationToken);
        scope.RecordResponse((int)response.StatusCode);
        return response;
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put ||
        method == HttpMethod.Patch || method == HttpMethod.Delete;
}
