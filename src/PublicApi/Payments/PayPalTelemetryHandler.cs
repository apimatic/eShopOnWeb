using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalTelemetryHandler : DelegatingHandler
{
    private readonly ILogger<PayPalTelemetryHandler> _logger;
    private readonly PayPalCallContext _context;

    public PayPalTelemetryHandler(ILogger<PayPalTelemetryHandler> logger, PayPalCallContext context)
    {
        _logger = logger;
        _context = context;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestId = request.Headers.TryGetValues("PayPal-Request-Id", out var values)
            ? values.FirstOrDefault()
            : null;
        _logger.LogInformation("PayPal {Method} {Host}{Path} requestId={RequestId}",
            request.Method.Method, request.RequestUri?.Host, request.RequestUri?.AbsolutePath, requestId);

        var response = await base.SendAsync(request, cancellationToken);
        _context.LastStatus = response.StatusCode;
        _logger.LogInformation("PayPal {Method} {Host}{Path} returned {StatusCode} requestId={RequestId}",
            request.Method.Method, request.RequestUri?.Host, request.RequestUri?.AbsolutePath,
            (int)response.StatusCode, requestId);
        return response;
    }
}
