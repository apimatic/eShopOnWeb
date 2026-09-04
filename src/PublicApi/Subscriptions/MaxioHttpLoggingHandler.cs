using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioHttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioHttpLoggingHandler> _logger;

    public MaxioHttpLoggingHandler(ILogger<MaxioHttpLoggingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Maxio request {Method} {Uri}", request.Method, request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogDebug("Maxio response {StatusCode} for {Method} {Uri}",
            (int)response.StatusCode, request.Method, request.RequestUri);
        return response;
    }
}
