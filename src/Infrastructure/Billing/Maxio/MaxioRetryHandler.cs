using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries transient Billing API failures with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// The provider limits concurrency rather than request rate and answers 429 when a caller pushes
/// past it, so backing off - rather than retrying immediately or fanning out - is the documented
/// way to recover. Only requests marked <see cref="SafeToRetryOption"/> are replayed: reads, and
/// writes that carry a duplicate-prevention token.
/// </remarks>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    internal static readonly HttpRequestOptionsKey<bool> SafeToRetryOption = new("Maxio.SafeToRetry");

    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly IOptions<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptions<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _settings.Value.MaxAttempts);
        var canRetry = (maxAttempts > 1) && IsSafeToRetry(request);

        // Snapshot the body and its headers up front: content cannot be sent a second time.
        byte[]? body = null;
        List<KeyValuePair<string, IEnumerable<string>>>? contentHeaders = null;
        if (canRetry && request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentHeaders = request.Content.Headers.ToList();
        }

        var method = request.Method;
        var path = request.RequestUri?.AbsolutePath;

        for (var attempt = 1; ; attempt++)
        {
            var attemptRequest = attempt == 1 ? request : Clone(request, body, contentHeaders);

            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            // Each attempt gets the full configured budget; the outer HttpClient timeout is
            // disabled so that backoff between attempts does not eat into it.
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.Value.TimeoutSeconds)));

            try
            {
                response = await base.SendAsync(attemptRequest, attemptCancellation.Token);
            }
            catch (HttpRequestException exception)
            {
                transportFailure = exception;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // This attempt timed out rather than the caller giving up.
                transportFailure = exception;
            }

            if (transportFailure is null && !IsTransient(response!.StatusCode))
            {
                return response;
            }

            if (!canRetry || (attempt >= maxAttempts))
            {
                if (transportFailure is not null)
                {
                    throw transportFailure;
                }

                return response!;
            }

            var delay = ComputeDelay(attempt, response);

            _logger.LogWarning(
                "Billing API {Method} {Path} failed ({Outcome}) on attempt {Attempt} of {MaxAttempts}; retrying in {DelayMs} ms.",
                method,
                path,
                transportFailure is not null ? transportFailure.GetType().Name : ((int)response!.StatusCode).ToString(),
                attempt,
                maxAttempts,
                (int)delay.TotalMilliseconds);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsSafeToRetry(HttpRequestMessage request)
    {
        if ((request.Method == HttpMethod.Get) || (request.Method == HttpMethod.Head))
        {
            return true;
        }

        return request.Options.TryGetValue(SafeToRetryOption, out var safe) && safe;
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.RequestTimeout => true,
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.InternalServerError => true,
        HttpStatusCode.BadGateway => true,
        HttpStatusCode.ServiceUnavailable => true,
        HttpStatusCode.GatewayTimeout => true,
        _ => false
    };

    private static TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            var hinted = retryAfter.Delta ?? (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null);
            if (hinted is { } wait && wait > TimeSpan.Zero)
            {
                return wait > MaxDelay ? MaxDelay : wait;
            }
        }

        var backoff = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        if (backoff > MaxDelay)
        {
            backoff = MaxDelay;
        }

        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, Math.Max(1, (int)(backoff.TotalMilliseconds / 2))));

        return backoff + jitter;
    }

    private static HttpRequestMessage Clone(
        HttpRequestMessage request,
        byte[]? body,
        List<KeyValuePair<string, IEnumerable<string>>>? contentHeaders)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            clone.Content.Headers.Clear();

            foreach (var header in contentHeaders ?? new List<KeyValuePair<string, IEnumerable<string>>>())
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
