using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Opt-in wire logging for the Visa integration, used to verify requests on a new integration. It logs
/// only the method, path/query and response status — never headers or bodies — so the HTTP Signature and
/// the secret it is derived from are never written to a log.
/// </summary>
public sealed class VisaHttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<VisaHttpLoggingHandler> _logger;

    public VisaHttpLoggingHandler(ILogger<VisaHttpLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("--> Visa {Method} {Path}", request.Method, request.RequestUri?.PathAndQuery);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("<-- Visa {Status} {Path}", (int)response.StatusCode, request.RequestUri?.PathAndQuery);

        // Diagnostic only (gated behind the debug flag): surface a provider error body server-side so a
        // rejected request can be understood. Never contains the secret. Buffered so the SDK can re-read it.
        if (!response.IsSuccessStatusCode && response.Content is not null)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("<-- Visa error body {Status}: {Body}", (int)response.StatusCode, body);
        }

        return response;
    }
}
