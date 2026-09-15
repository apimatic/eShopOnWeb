using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioRequestSender
{
    private const int MaxTries = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(30);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        Func<HttpRequestMessage> createRequest,
        bool retryServerErrors,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            response?.Dispose();
            using var request = createRequest();
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!retryServerErrors || attempt == MaxTries - 1)
                {
                    throw;
                }

                await BackoffAsync(attempt, retryAfter: null, cancellationToken);
                continue;
            }

            if (ShouldRetry(response.StatusCode, retryServerErrors) && attempt < MaxTries - 1)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                logger.LogWarning("Twilio returned {StatusCode}; retrying with backoff.", (int)response.StatusCode);
                await BackoffAsync(attempt, retryAfter, cancellationToken);
                continue;
            }

            return response;
        }

        return response!;
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        int? code = null;
        var message = payload;
        try
        {
            var error = JsonSerializer.Deserialize<TwilioErrorDto>(payload, JsonOptions);
            if (error is not null)
            {
                code = error.Code;
                message = error.Message ?? payload;
            }
        }
        catch (JsonException)
        {
            // keep raw payload
        }

        throw new TwilioApiException(response.StatusCode, code, TwilioHttp.Sanitize(message));
    }

    private static bool ShouldRetry(HttpStatusCode status, bool retryServerErrors)
    {
        if (status == HttpStatusCode.TooManyRequests || status == HttpStatusCode.ServiceUnavailable)
        {
            return true;
        }

        return retryServerErrors && status == HttpStatusCode.InternalServerError;
    }

    private static Task BackoffAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        TimeSpan delay;
        if (retryAfter.HasValue)
        {
            delay = retryAfter.Value;
        }
        else
        {
            var windowMs = Math.Min(Cap.TotalMilliseconds, BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
            delay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * windowMs);
        }

        return Task.Delay(delay, cancellationToken);
    }
}
