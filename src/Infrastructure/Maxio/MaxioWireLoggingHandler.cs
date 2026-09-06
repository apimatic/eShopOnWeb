using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Logs the method, URL and status of every provider call when <c>Maxio:LogRequests</c> is on.
/// </summary>
/// <remarks>
/// A successful SDK call surfaces only the deserialized body — never the request URL or status — so a
/// wrong verb or an unsubstituted path placeholder compiles cleanly and shows up only as a runtime 404.
/// This is the seam that makes the outgoing request observable. It logs no headers and no bodies, so
/// credentials cannot leak into the log.
/// </remarks>
internal sealed class MaxioWireLoggingHandler : DelegatingHandler
{
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioWireLoggingHandler> _logger;

    public MaxioWireLoggingHandler(IOptions<MaxioSettings> settings, ILogger<MaxioWireLoggingHandler> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_settings.LogRequests)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Maxio --> {Method} {Uri}", request.Method, request.RequestUri);

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            _logger.LogInformation(
                "Maxio <-- {StatusCode} {Method} {Uri} in {ElapsedMilliseconds}ms",
                (int)response.StatusCode, request.Method, request.RequestUri, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(
                "Maxio <-- {ExceptionType} for {Method} {Uri} after {ElapsedMilliseconds}ms",
                ex.GetType().Name, request.Method, request.RequestUri, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
