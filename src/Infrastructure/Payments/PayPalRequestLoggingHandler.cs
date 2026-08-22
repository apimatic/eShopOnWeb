using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Logs method, path, and status only. Never logs bodies (PAN/CVC) or Authorization headers.
/// </summary>
public sealed class PayPalRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalRequestLoggingHandler> _logger;

    public PayPalRequestLoggingHandler(ILogger<PayPalRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.GetLeftPart(System.UriPartial.Path);
        _logger.LogInformation("PayPal --> {Method} {Path}", request.Method, path);

        var response = await base.SendAsync(request, cancellationToken);

        _logger.LogInformation("PayPal <-- {StatusCode} {Method} {Path}",
            (int)response.StatusCode, request.Method, path);

        return response;
    }
}
