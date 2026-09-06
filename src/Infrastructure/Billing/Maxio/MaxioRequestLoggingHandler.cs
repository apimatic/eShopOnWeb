using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Logs the verb, path and status of every Maxio call. The SDK has no logging hook of its own, and on a
/// successful response it surfaces only the deserialized body — never the URL or status — so a wrong verb or
/// a mis-substituted path parameter has no in-band signal at all. Enable
/// <c>Maxio:LogRequests</c> when bringing a new call up on the wire.
/// </summary>
/// <remarks>Query strings are not logged: they can carry a customer reference.</remarks>
internal sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private readonly IOptions<MaxioSettings> _settings;
    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(IOptions<MaxioSettings> settings, ILogger<MaxioRequestLoggingHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_settings.Value.LogRequests)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Maxio {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                request.Method.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogDebug(ex, "Maxio {Method} {Path} failed after {ElapsedMs}ms",
                request.Method.Method, request.RequestUri?.AbsolutePath, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
