using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalSingleWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var isToken = path.Contains("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase);
        var isWrite = request.Method != HttpMethod.Get
            && request.Method != HttpMethod.Head
            && request.Method != HttpMethod.Options;

        if (isWrite && !isToken)
        {
            PayPalCallContext.CountWriteOrThrow();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
