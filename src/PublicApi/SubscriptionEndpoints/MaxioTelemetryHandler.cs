using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioTelemetryHandler : DelegatingHandler
{
    private readonly ILogger<MaxioTelemetryHandler> _logger;

    public MaxioTelemetryHandler(ILogger<MaxioTelemetryHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var path = request.RequestUri?.AbsolutePath ?? "unknown";
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            _logger.LogInformation(
                "Maxio {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms",
                request.Method,
                path,
                (int)response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Maxio {Method} {Path} failed with {ExceptionType} after {ElapsedMilliseconds} ms",
                request.Method,
                path,
                exception.GetType().Name,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
