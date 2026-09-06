using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Logs the verb, URL and status of every Maxio call at <c>Debug</c> level.
/// </summary>
/// <remarks>
/// The SDK surfaces neither the request URL nor the status on a successful response, so a wrong verb or an
/// unsubstituted path template compiles cleanly and only shows up as a runtime 404. This handler is how a
/// newly written call gets verified on the wire. It never logs headers or bodies, so the API key and
/// customer data stay out of the log.
/// </remarks>
internal sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(ILogger<MaxioRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug("Maxio --> {Method} {Uri}", request.Method, request.RequestUri);
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            _logger.LogDebug("Maxio <-- {StatusCode} {Method} {Uri} in {ElapsedMs}ms",
                (int)response.StatusCode, request.Method, request.RequestUri, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogDebug(ex, "Maxio <-- {ExceptionType} {Method} {Uri} after {ElapsedMs}ms",
                ex.GetType().Name, request.Method, request.RequestUri, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
