using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(ILogger<MaxioRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Maxio {Method} {Uri}", request.Method, request.RequestUri);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Maxio {StatusCode} {Method} {Uri}", (int)response.StatusCode, request.Method, request.RequestUri);
        }

        return response;
    }
}
