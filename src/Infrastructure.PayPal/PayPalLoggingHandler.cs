using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Logs PayPal request method/path and response status, and records the status for the error
/// boundary. Bodies are never logged: requests may carry full card details.
/// </summary>
public class PayPalLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalLoggingHandler> _logger;

    public PayPalLoggingHandler(ILogger<PayPalLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        PayPalResponseStatusTracker.LastStatus = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug("PayPal {Method} {Path} responded {StatusCode}",
                request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode);
        }
        else
        {
            _logger.LogWarning("PayPal {Method} {Path} responded {StatusCode}",
                request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode);
        }

        return response;
    }
}
