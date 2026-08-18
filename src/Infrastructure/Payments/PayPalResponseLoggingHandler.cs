using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Diagnostic handler that logs the PayPal request line (method + URI only) and the RESPONSE status and body.
/// Request bodies are never logged because they can carry raw card details. Off by default; enable with
/// <c>PayPal:WireLog=true</c> for first-run wire verification only.
/// </summary>
public sealed class PayPalResponseLoggingHandler : DelegatingHandler
{
    private readonly IAppLogger<PayPalResponseLoggingHandler> _logger;

    public PayPalResponseLoggingHandler(IAppLogger<PayPalResponseLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _logger.LogWarning($"PayPal --> {request.Method} {request.RequestUri}");
        var response = await base.SendAsync(request, ct);

        // Response bodies do not carry full card details (only last four), so they are safe to log.
        var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(ct);
        if (body.Length > 4000)
        {
            body = body.Substring(0, 4000) + "…";
        }
        _logger.LogWarning($"PayPal <-- {(int)response.StatusCode} {request.RequestUri?.AbsolutePath}\n{body}");
        return response;
    }
}
