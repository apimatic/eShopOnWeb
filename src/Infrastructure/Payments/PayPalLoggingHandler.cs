using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Logs method, URI and status of every PayPal call when PayPal:LogHttp is set.
/// Request and response bodies are deliberately never logged: they can carry
/// card details.
/// </summary>
public sealed class PayPalLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalLoggingHandler> _logger;
    private readonly bool _enabled;

    public PayPalLoggingHandler(ILogger<PayPalLoggingHandler> logger, IOptions<PayPalSettings> settings)
    {
        _logger = logger;
        _enabled = settings.Value.LogHttp;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_enabled)
        {
            _logger.LogWarning("PayPal --> {Method} {Uri}", request.Method, request.RequestUri);
        }
        var response = await base.SendAsync(request, cancellationToken);
        if (_enabled)
        {
            _logger.LogWarning("PayPal <-- {StatusCode} for {Method} {Uri}",
                (int)response.StatusCode, request.Method, request.RequestUri);
        }
        return response;
    }
}
