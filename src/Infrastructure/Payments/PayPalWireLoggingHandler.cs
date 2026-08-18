using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Optional diagnostic handler (off by default) for verifying PayPal calls on the wire. It logs the
/// request line and the response status + body. It deliberately never logs the request body, because
/// authorize/vault requests carry raw card data; PayPal response bodies never echo a full PAN.
/// </summary>
public class PayPalWireLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalWireLoggingHandler> _logger;
    private readonly IOptions<PayPalSettings> _settings;

    public PayPalWireLoggingHandler(ILogger<PayPalWireLoggingHandler> logger, IOptions<PayPalSettings> settings)
    {
        _logger = logger;
        _settings = settings;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_settings.Value.WireLog)
            return await base.SendAsync(request, cancellationToken);

        _logger.LogInformation("PayPal --> {Method} {Uri}", request.Method, request.RequestUri);

        var response = await base.SendAsync(request, cancellationToken);

        string body = string.Empty;
        if (response.Content is not null)
        {
            // Buffer the content so reading it here does not consume the stream the SDK deserializes.
            await response.Content.LoadIntoBufferAsync();
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 4000)
                body = body.Substring(0, 4000) + "...(truncated)";
        }

        _logger.LogInformation("PayPal <-- {Status} {Method} {Uri}\n{Body}",
            (int)response.StatusCode, request.Method, request.RequestUri, body);

        return response;
    }
}
