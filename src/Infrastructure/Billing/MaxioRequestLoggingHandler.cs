using System;
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
        _logger.LogInformation("Maxio {Method} {Path}", request.Method, request.RequestUri?.PathAndQuery);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Maxio {Method} {Path} -> {StatusCode}", request.Method, request.RequestUri?.PathAndQuery, (int)response.StatusCode);
        return response;
    }
}
