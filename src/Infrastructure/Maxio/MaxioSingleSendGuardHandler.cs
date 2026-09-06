using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Enforces <see cref="MaxioSingleSendScope"/> on the outbound pipeline. Outside a scope every request
/// passes through untouched, so reads keep their retries.
/// </summary>
public sealed class MaxioSingleSendGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var scope = MaxioSingleSendScope.Current;

        if (scope is not null && !scope.TryClaimSend())
        {
            throw new MaxioDuplicateSendBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
