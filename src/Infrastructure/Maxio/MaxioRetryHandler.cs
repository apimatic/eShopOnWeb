using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient failures for safe (idempotent) requests only. GETs are retried on 429/5xx and
/// transport faults with a short linear back-off. Non-idempotent requests (POSTs that create a
/// customer or subscription) are never retried here, to avoid creating duplicates.
/// </summary>
public class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(300);

    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (attempt >= MaxAttempts || !IsTransient(response.StatusCode))
                {
                    return response;
                }

                _logger.LogWarning(
                    "Transient Maxio response {StatusCode} for {Method} {Uri} (attempt {Attempt}/{Max}); retrying.",
                    (int)response.StatusCode, request.Method, request.RequestUri, attempt, MaxAttempts);
                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex,
                    "Transient transport error calling Maxio {Method} {Uri} (attempt {Attempt}/{Max}); retrying.",
                    request.Method, request.RequestUri, attempt, MaxAttempts);
            }

            await Task.Delay(BaseDelay * attempt, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.BadGateway => true,
        HttpStatusCode.ServiceUnavailable => true,
        HttpStatusCode.GatewayTimeout => true,
        _ => false,
    };
}
