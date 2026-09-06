using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Logs the verb, URI and status of every Maxio request.
/// </summary>
/// <remarks>
/// This is the only way to see a Maxio request: on a successful response the SDK returns the
/// deserialized body and nothing else — no URL, no status — so a wrong verb or an unsubstituted path
/// placeholder compiles cleanly and shows up only as a runtime 404. Bodies are never logged: they carry
/// customer data, and the request carries the API key.
/// </remarks>
public sealed class MaxioLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioLoggingHandler> _logger;

    public MaxioLoggingHandler(ILogger<MaxioLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            _logger.LogDebug(
                "Maxio {Method} {Uri} -> {StatusCode} in {ElapsedMs}ms",
                request.Method,
                request.RequestUri,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Maxio {Method} {Uri} -> failed after {ElapsedMs}ms",
                request.Method,
                request.RequestUri,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}
