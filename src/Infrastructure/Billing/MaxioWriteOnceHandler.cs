using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Enforces the "at most one send" guarantee of an ambient <see cref="SingleSendScope"/>.
/// Outside such a scope it is inert, so reads keep the SDK's normal retry behaviour.
/// </summary>
public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = SingleSendScope.Current;
        if (scope is not null && !scope.TryClaimSend())
        {
            throw new DuplicateSendBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
