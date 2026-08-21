using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Logs method and path only. Never reads request or response bodies (PAN/CVV).
/// </summary>
public sealed class PayPalRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalRequestLoggingHandler> _logger;

    public PayPalRequestLoggingHandler(ILogger<PayPalRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        _logger.LogInformation("PayPal {Method} {Path}", request.Method, path);

        var response = await base.SendAsync(request, cancellationToken);
        PayPalCallContext.LastStatusCode = (int)response.StatusCode;
        _logger.LogInformation("PayPal {Method} {Path} -> {Status}", request.Method, path, (int)response.StatusCode);
        return response;
    }
}
