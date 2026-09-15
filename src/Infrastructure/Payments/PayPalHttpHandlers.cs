using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (path.Contains("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase))
        {
            return base.SendAsync(request, cancellationToken);
        }

        var isWrite = request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete;

        if (isWrite && !PayPalWriteGuard.TryMarkSent())
        {
            throw new PayPalDuplicateSendException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        PayPalLastStatus.Code.Value = (int)response.StatusCode;
        return response;
    }
}
