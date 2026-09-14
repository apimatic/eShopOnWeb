using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioLoggingHandler(ILogger<MaxioLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            logger.LogInformation(
                "Maxio {Method} completed with HTTP {StatusCode} in {ElapsedMilliseconds}ms",
                request.Method.Method,
                (int)response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                "Maxio {Method} transport failed after {ElapsedMilliseconds}ms",
                request.Method.Method,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
