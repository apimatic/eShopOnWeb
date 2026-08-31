using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// An optional diagnostic handler (gated by <see cref="VisaSettings.LogWire"/>) that logs the outgoing
/// request line and the response status and body at Debug level. It exists to verify a new integration on
/// the wire — path/verb/query and the exact response shape — since the SDK surfaces none of that on a
/// successful call. It never logs request headers, so the signature and secret are never written out.
/// </summary>
internal sealed class VisaWireLogHandler : DelegatingHandler
{
    private const int MaxBodyChars = 4000;

    private readonly ILogger<VisaWireLogHandler> _logger;

    public VisaWireLogHandler(ILogger<VisaWireLogHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("VISA WIRE --> {Method} {Uri}", request.Method, request.RequestUri);

        var response = await base.SendAsync(request, cancellationToken);

        string body;
        try
        {
            // Response content is buffered by HttpClient, so reading it here does not disturb the SDK's read.
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            body = $"(could not read body: {ex.GetType().Name})";
        }

        if (body.Length > MaxBodyChars)
        {
            body = body.Substring(0, MaxBodyChars) + "…(truncated)";
        }

        _logger.LogDebug("VISA WIRE <-- {Status} {Method} {Uri} body={Body}",
            (int)response.StatusCode, request.Method, request.RequestUri, body);

        return response;
    }
}
