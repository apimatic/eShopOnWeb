using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var state = MaxioCallScope.Current;
        if (state?.WriteOnce == true && request.Method == HttpMethod.Post && state.RecordWriteSend() > 1)
        {
            throw new MaxioWriteReplayBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class MaxioDiagnosticsHandler : DelegatingHandler
{
    private readonly ILogger<MaxioDiagnosticsHandler> _logger;

    public MaxioDiagnosticsHandler(ILogger<MaxioDiagnosticsHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (MaxioCallScope.Current is { } state)
            {
                state.LastStatusCode = response.StatusCode;
            }

            _logger.LogInformation(
                "Maxio {Method} {Path} completed with {StatusCode} in {ElapsedMilliseconds}ms",
                request.Method.Method,
                request.RequestUri?.AbsolutePath,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(
                "Maxio {Method} {Path} failed after {ElapsedMilliseconds}ms",
                request.Method.Method,
                request.RequestUri?.AbsolutePath,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
