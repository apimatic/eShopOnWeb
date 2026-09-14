using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Optional diagnostic handler: logs each Maxio request line and, for non-success responses, the raw
/// response body. Enabled only when <c>Maxio:DebugWireLogging</c> is true. Gated because the body can
/// contain request/response detail that should not be logged in production by default.
/// </summary>
public sealed class MaxioWireLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioWireLoggingHandler> _logger;

    public MaxioWireLoggingHandler(ILogger<MaxioWireLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string body = string.Empty;
            try { body = await response.Content.ReadAsStringAsync(cancellationToken); } catch { /* best effort */ }
            _logger.LogWarning("Maxio {Method} {Uri} -> {Status}. Body: {Body}",
                request.Method, request.RequestUri, (int)response.StatusCode, body);
        }
        else
        {
            _logger.LogInformation("Maxio {Method} {Uri} -> {Status}",
                request.Method, request.RequestUri, (int)response.StatusCode);
        }
        return response;
    }
}
