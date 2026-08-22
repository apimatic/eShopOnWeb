using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Logs PayPal HTTP method, path and status only. Never logs request or response bodies
/// (those can contain card numbers).
/// </summary>
public sealed class PayPalRedactingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalRedactingHandler> _logger;

    public PayPalRedactingHandler(ILogger<PayPalRedactingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.GetLeftPart(System.UriPartial.Path);
        _logger.LogInformation("PayPal {Method} {Path}", request.Method, path);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("PayPal {Method} {Path} -> {StatusCode}", request.Method, path, (int)response.StatusCode);
        return response;
    }
}
