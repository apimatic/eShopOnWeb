using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Logs outbound billing calls. A successful SDK call surfaces only the deserialized body - never
/// the URL or the status - so without this there is no in-band way to confirm that a path template,
/// verb or query parameter came out the way it was meant to.
/// </summary>
/// <remarks>
/// Debug logs the verb and path only. The full URI is logged at Trace, because query values can
/// carry the shopper's reference. Request bodies and the Authorization header are never logged.
/// </remarks>
internal sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(ILogger<MaxioRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Maxio --> {Method} {Path}", request.Method, request.RequestUri?.AbsolutePath);
        _logger.LogTrace("Maxio --> {Method} {Uri}", request.Method, request.RequestUri);

        var stopwatch = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken);
        stopwatch.Stop();

        _logger.LogDebug(
            "Maxio <-- {StatusCode} {Method} {Path} in {ElapsedMilliseconds} ms",
            (int)response.StatusCode,
            request.Method,
            request.RequestUri?.AbsolutePath,
            stopwatch.ElapsedMilliseconds);

        return response;
    }
}
