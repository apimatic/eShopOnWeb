using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalSafeLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalSafeLoggingHandler> _logger;

    public PayPalSafeLoggingHandler(ILogger<PayPalSafeLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("PayPal {Method} {Path}", request.Method, request.RequestUri?.PathAndQuery);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("PayPal {Method} {Path} -> {StatusCode}", request.Method, request.RequestUri?.PathAndQuery, (int)response.StatusCode);
        return response;
    }
}
