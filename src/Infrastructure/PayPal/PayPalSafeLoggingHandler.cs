using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public sealed class PayPalSafeLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalSafeLoggingHandler> _logger;

    public PayPalSafeLoggingHandler(ILogger<PayPalSafeLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("PayPal {Method} {Path}", request.Method, request.RequestUri?.AbsolutePath);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("PayPal {Method} {Path} -> {Status}", request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode);
        return response;
    }
}
