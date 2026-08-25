using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient failures (429 + 5xx, connection errors) for idempotent GET requests only.
/// POSTs are never retried blindly: a lost response could otherwise create a duplicate
/// customer/subscription. Idempotency for writes is handled at the service level instead.
/// </summary>
public class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(300);

    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isIdempotentGet = request.Method == HttpMethod.Get;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportError = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (isIdempotentGet && attempt < MaxAttempts)
            {
                transportError = ex;
            }

            var retryable = response is not null &&
                ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500);

            if (!isIdempotentGet || attempt >= MaxAttempts || (transportError is null && !retryable))
            {
                if (transportError is not null)
                {
                    throw transportError;
                }
                return response!;
            }

            var delay = response?.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

            _logger.LogWarning(
                "Maxio request {Method} {Path} failed (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}ms.",
                request.Method, request.RequestUri?.AbsolutePath, attempt, MaxAttempts, delay.TotalMilliseconds);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }
}
