using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Optional diagnostic handler that logs the outgoing request line and the response status + body for
/// Visa calls. Enabled only when configuration flag <c>Visa:WireLog</c> is true; never logs the
/// Authorization/signature headers or any secret. Intended for verifying a new integration on the wire.
/// </summary>
public sealed class VisaWireLoggingHandler : DelegatingHandler
{
    private readonly ILogger<VisaWireLoggingHandler> _logger;

    public VisaWireLoggingHandler(ILogger<VisaWireLoggingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("Visa --> {Method} {Uri} body={Body}", request.Method, request.RequestUri, requestBody);

        var response = await base.SendAsync(request, ct);

        var responseBody = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("Visa <-- {Status} body={Body}", (int)response.StatusCode, responseBody);
        return response;
    }
}
