using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Logs the verb, URL and status of every Maxio call. The SDK surfaces neither the request URL nor the
/// status on a successful response, so a wrong verb or a mis-substituted path segment compiles cleanly
/// and shows up only as a runtime 404. Enable with <c>Maxio:LogRequests</c> when wiring a new call, then
/// turn it back off.
/// </summary>
public sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(ILogger<MaxioRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            _logger.LogInformation(
                "Maxio {Method} {Uri} -> {StatusCode} in {ElapsedMs}ms",
                request.Method,
                request.RequestUri,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Maxio {Method} {Uri} failed after {ElapsedMs}ms",
                request.Method,
                request.RequestUri,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
