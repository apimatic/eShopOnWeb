using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries safe Maxio GET lookups on transient failures. POST is not retried here because
/// subscription/customer creation is reconciled by the billing service using unique references.
/// </summary>
internal sealed class MaxioTransientRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private readonly ILogger<MaxioTransientRetryHandler> _logger;

    public MaxioTransientRetryHandler(ILogger<MaxioTransientRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Transient Maxio GET failure for {Path} (attempt {Attempt}/{Max}).",
                    request.RequestUri, attempt, MaxAttempts);
                await DelayAsync(attempt, cancellationToken);
                continue;
            }

            if (response is not null && IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                _logger.LogWarning("Transient Maxio status {StatusCode} for {Path} (attempt {Attempt}/{Max}).",
                    (int)response.StatusCode, request.RequestUri, attempt, MaxAttempts);
                await DelayAsync(attempt, cancellationToken);
                continue;
            }

            return response!;
        }

        return response!;
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.TooManyRequests ||
        statusCode == System.Net.HttpStatusCode.BadGateway ||
        statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
        statusCode == System.Net.HttpStatusCode.GatewayTimeout;

    private static Task DelayAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)), cancellationToken);
}
