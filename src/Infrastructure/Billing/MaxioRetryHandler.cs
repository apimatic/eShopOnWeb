using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();
            response = await base.SendAsync(request, cancellationToken);

            if (request.Method != HttpMethod.Get || !ShouldRetry(response.StatusCode) || attempt == MaxAttempts)
            {
                return response;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
        }

        return response!;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == HttpStatusCode.TooManyRequests || code >= 500;
    }
}
