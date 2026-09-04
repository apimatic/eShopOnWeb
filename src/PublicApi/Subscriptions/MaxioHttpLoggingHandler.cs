using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioHttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioHttpLoggingHandler> _logger;

    public MaxioHttpLoggingHandler(ILogger<MaxioHttpLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Maxio request {Method} {Path}", request.Method, request.RequestUri?.AbsolutePath);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogDebug("Maxio response {StatusCode} for {Method} {Path}",
            (int)response.StatusCode,
            request.Method,
            request.RequestUri?.AbsolutePath);
        return response;
    }
}
