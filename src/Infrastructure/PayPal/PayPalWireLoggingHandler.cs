using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Diagnostic handler that logs the request line and the response status/body for PayPal calls.
/// It deliberately never logs the request body, which can contain a card number — only responses,
/// which carry no full card details. Enabled only when PayPal:WireLog is turned on.
/// </summary>
public sealed class PayPalWireLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalWireLoggingHandler> _logger;

    public PayPalWireLoggingHandler(ILogger<PayPalWireLoggingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("PayPal --> {Method} {Uri}", request.Method, request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken);
        var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("PayPal <-- {Status} {Method} {Uri} :: {Body}",
            (int)response.StatusCode, request.Method, request.RequestUri, body);
        return response;
    }
}
