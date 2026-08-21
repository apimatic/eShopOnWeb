using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalAuthenticationHandler : DelegatingHandler
{
    private readonly PayPalAccessTokenService _tokens;
    private readonly ILogger<PayPalAuthenticationHandler> _logger;

    public PayPalAuthenticationHandler(PayPalAccessTokenService tokens, ILogger<PayPalAuthenticationHandler> logger)
    {
        _tokens = tokens;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _logger.LogInformation("PayPal {Method} {Path}", request.Method, request.RequestUri?.PathAndQuery);
        return await base.SendAsync(request, cancellationToken);
    }
}
