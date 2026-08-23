using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(ILogger<MaxioRequestLoggingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            _logger.LogInformation(
                "Maxio {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds}ms",
                request.Method,
                request.RequestUri?.AbsolutePath,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Maxio {Method} {Path} failed after {ElapsedMilliseconds}ms",
                request.Method,
                request.RequestUri?.AbsolutePath,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
