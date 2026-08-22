using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public abstract class TwilioHttpClientBase
{
    private readonly TwilioSettings _settings;
    private readonly ILogger _logger;
    private readonly Random _jitter = new();

    protected TwilioHttpClientBase(IOptions<TwilioSettings> options, ILogger logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    protected TwilioSettings Settings => _settings;

    protected AuthenticationHeaderValue CreateBasicAuthHeader()
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    protected async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient httpClient,
        Func<HttpRequestMessage> requestFactory,
        bool retryServerErrors,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();
            request.Headers.Authorization = CreateBasicAuthHeader();
            response = await httpClient.SendAsync(request, cancellationToken);

            var status = (int)response.StatusCode;
            var retryable = status == 429 || (retryServerErrors && status >= 500);
            if (!retryable || attempt == maxAttempts)
            {
                return response;
            }

            var delay = GetRetryDelay(response, attempt);
            _logger.LogWarning("Twilio HTTP {StatusCode} on attempt {Attempt}; retrying after {DelayMs} ms.",
                status, attempt, (int)delay.TotalMilliseconds);
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    protected async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var httpStatus = (int)response.StatusCode;
        int? providerCode = null;
        try
        {
            var payload = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(payload))
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var code))
                {
                    providerCode = code;
                }
            }
        }
        catch (JsonException)
        {
            // Swallow parse failures; the HTTP status is enough to fail the call without logging bodies.
        }

        _logger.LogWarning("Twilio {Operation} failed with HTTP {StatusCode} and provider code {ProviderCode}.",
            operation, httpStatus, providerCode);

        throw new TwilioApiException(httpStatus, providerCode, $"{operation} failed.");
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            return retryAfter;
        }

        if (response.Headers.RetryAfter?.Date is { } retryAt)
        {
            var until = retryAt - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        var windowMs = (int)Math.Min(8000, 200 * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(_jitter.Next(0, windowMs));
    }
}

internal sealed class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatus, int? providerCode, string message) : base(message)
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
    }

    public int HttpStatus { get; }
    public int? ProviderCode { get; }
}
