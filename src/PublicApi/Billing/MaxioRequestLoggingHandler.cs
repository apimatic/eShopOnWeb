using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Logs verb + path only (never headers or bodies — Basic auth would leak the API key).
/// </summary>
internal sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(ILogger<MaxioRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Maxio {Method} {Path}", request.Method, request.RequestUri?.PathAndQuery);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("Maxio {Status} {Method} {Path}", (int)response.StatusCode, request.Method, request.RequestUri?.PathAndQuery);
        return response;
    }
}
