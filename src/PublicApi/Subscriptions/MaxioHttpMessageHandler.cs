using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Records the provider request target without logging credentials or request bodies.
/// This keeps first-run wire verification available without exposing sensitive data.
/// </summary>
public sealed class MaxioHttpMessageHandler : DelegatingHandler
{
    private readonly ILogger<MaxioHttpMessageHandler> _logger;

    public MaxioHttpMessageHandler(ILogger<MaxioHttpMessageHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Maxio request {Method} {Uri}", request.Method, request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogDebug("Maxio response {StatusCode} for {Method} {Uri}",
            (int)response.StatusCode, request.Method, request.RequestUri);
        return response;
    }
}
