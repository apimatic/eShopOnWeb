using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries transient Advanced Billing failures with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// Advanced Billing rate limits per site and answers with HTTP 429; it does not send a
/// <c>Retry-After</c> header, so the backoff is computed client side. A <c>Retry-After</c> is still
/// honoured if one ever appears.
/// <para>
/// Writes are retried as well: every write this integration issues carries an application-supplied
/// <c>reference</c> that the provider enforces as unique, so a retry of a request that did land is
/// rejected with a duplicate-reference error which the caller resolves by looking the record up.
/// </para>
/// </remarks>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptions<MaxioOptions> options, ILogger<MaxioRetryHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                transportFailure = ex;
            }

            if (attempt >= _options.MaxRetryAttempts)
            {
                if (response is not null)
                {
                    return response;
                }

                throw transportFailure!;
            }

            var delay = ComputeDelay(attempt, response);

            _logger.LogWarning(
                "Maxio API {Method} {Path} failed transiently ({Outcome}); retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts}).",
                request.Method.Method,
                request.RequestUri?.AbsolutePath,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure!.GetType().Name,
                (int)delay.TotalMilliseconds,
                attempt + 1,
                _options.MaxRetryAttempts);

            response?.Dispose();
            attempt++;

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        var baseDelayMs = Math.Max(1, _options.RetryBaseDelayMilliseconds);
        var backoffMs = baseDelayMs * Math.Pow(2, attempt);
        var jitterMs = Random.Shared.Next(0, baseDelayMs);

        return TimeSpan.FromMilliseconds(Math.Min(backoffMs + jitterMs, 30_000));
    }
}
