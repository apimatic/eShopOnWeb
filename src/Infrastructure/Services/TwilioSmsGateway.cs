using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class TwilioSmsGateway : ISmsGateway
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupClientName = "TwilioLookup";

    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const int MaxAttempts = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioSmsGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, BuildLookupUrl(path));
        using var response = await SendWithRetryAsync(LookupClientName, request, retryOnServerError: true, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult { Valid = false, ValidationErrors = new[] { "NOT_A_NUMBER" } };
        }

        var payload = await ReadJsonAsync<TwilioLookupResponse>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || payload == null)
        {
            throw new InvalidOperationException($"Phone number lookup failed with HTTP {(int)response.StatusCode}.");
        }

        return new PhoneNumberLookupResult
        {
            Valid = payload.Valid,
            CanonicalE164 = payload.PhoneNumber,
            NationalFormat = payload.NationalFormat,
            ValidationErrors = payload.ValidationErrors ?? new List<string>()
        };
    }

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.ToE164),
            new("Body", request.Body),
            new("From", _settings.FromNumber),
            new("MessagingServiceSid", _settings.MessagingServiceSid)
        };

        if (request.SendAt.HasValue)
        {
            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>(
                "SendAt",
                request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        var url = BuildMessagingUrl($"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json");
        using var httpRequest = CreateFormRequest(HttpMethod.Post, url, fields);

        HttpResponseMessage? response = null;
        try
        {
            response = await SendWithRetryAsync(MessagingClientName, httpRequest, retryOnServerError: false, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var resource = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<TwilioMessageResource>(json, JsonOptions);

            if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300 && resource?.Sid != null)
            {
                return new SmsSendResult
                {
                    Accepted = true,
                    Message = ToSnapshot(resource)
                };
            }

            var error = TryReadError(resource, json);
            _logger.LogWarning(
                "Twilio create message was not accepted. HTTP {StatusCode}, provider code {ErrorCode}",
                (int)response.StatusCode,
                error.ErrorCode);
            return new SmsSendResult
            {
                Accepted = false,
                FailureStatus = "failed",
                ErrorCode = error.ErrorCode,
                Message = resource != null ? ToSnapshot(resource) : null
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Twilio create message timed out; not retrying to avoid a duplicate send");
            return new SmsSendResult { Accepted = false, FailureStatus = "failed" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Twilio create message failed: {Message}", ex.Message);
            return new SmsSendResult { Accepted = false, FailureStatus = "failed" };
        }
        finally
        {
            response?.Dispose();
        }
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var url = BuildMessagingUrl(
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json");
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await SendWithRetryAsync(MessagingClientName, request, retryOnServerError: true, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var resource = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || resource == null)
        {
            throw new InvalidOperationException($"Fetching message failed with HTTP {(int)response.StatusCode}.");
        }

        return ToSnapshot(resource);
    }

    public Task<SmsMessageSnapshot?> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(
            providerMessageSid,
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            cancellationToken);
    }

    public Task<SmsMessageSnapshot?> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(
            providerMessageSid,
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentByConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>
        {
            ["From"] = _settings.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };

        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json?{ToQueryString(query)}";
        var nextUrl = BuildMessagingUrl(path);
        var results = new List<SmsMessageSnapshot>();

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = CreateRequest(HttpMethod.Get, nextUrl);
            using var response = await SendWithRetryAsync(MessagingClientName, request, retryOnServerError: true, cancellationToken);
            var page = await ReadJsonAsync<TwilioMessageListResponse>(response, cancellationToken);
            if (!response.IsSuccessStatusCode || page == null)
            {
                throw new InvalidOperationException($"Listing messages failed with HTTP {(int)response.StatusCode}.");
            }

            if (page.Messages != null)
            {
                results.AddRange(page.Messages.Select(ToSnapshot));
            }

            nextUrl = ResolveMessagingNextPage(page.NextPageUri);
        }

        return results;
    }

    private async Task<SmsMessageSnapshot?> UpdateMessageAsync(
        string providerMessageSid,
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var url = BuildMessagingUrl(
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json");
        using var request = CreateFormRequest(HttpMethod.Post, url, fields);
        using var response = await SendWithRetryAsync(MessagingClientName, request, retryOnServerError: true, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var resource = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || resource == null)
        {
            throw new InvalidOperationException($"Updating message failed with HTTP {(int)response.StatusCode}.");
        }

        return ToSnapshot(resource);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string clientName,
        HttpRequestMessage request,
        bool retryOnServerError,
        CancellationToken cancellationToken)
    {
        var method = request.Method;
        var uri = request.RequestUri ?? throw new InvalidOperationException("Twilio request is missing a URI.");
        var contentBytes = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = request.Content?.Headers.ContentType?.ToString();

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await DelayForRetryAsync(response, attempt, cancellationToken);
                response?.Dispose();
            }

            using var attemptRequest = new HttpRequestMessage(method, uri);
            attemptRequest.Headers.Authorization = BuildBasicAuth();
            attemptRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (contentBytes != null)
            {
                attemptRequest.Content = new ByteArrayContent(contentBytes);
                if (!string.IsNullOrEmpty(contentType))
                {
                    attemptRequest.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
            }

            var client = _httpClientFactory.CreateClient(clientName);
            response = await client.SendAsync(attemptRequest, cancellationToken);
            if (!ShouldRetry(response, retryOnServerError) || attempt == MaxAttempts - 1)
            {
                return response;
            }
        }

        throw new InvalidOperationException("Twilio request retry loop exited unexpectedly.");
    }

    private static bool ShouldRetry(HttpResponseMessage response, bool retryOnServerError)
    {
        if ((int)response.StatusCode == 429)
        {
            return true;
        }

        return retryOnServerError && (int)response.StatusCode == 503;
    }

    private static async Task DelayForRetryAsync(HttpResponseMessage? response, int attempt, CancellationToken cancellationToken)
    {
        if (response?.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
        {
            await Task.Delay(retryAfter, cancellationToken);
            return;
        }

        var windowMs = Math.Min(30_000, 500 * Math.Pow(2, attempt - 1));
        var jitter = Random.Shared.Next(0, (int)windowMs + 1);
        await Task.Delay(jitter, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = BuildBasicAuth();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private HttpRequestMessage CreateFormRequest(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var request = CreateRequest(method, url);
        var encoded = string.Join("&", fields.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value ?? string.Empty)));
        request.Content = new StringContent(encoded, Encoding.UTF8, "application/x-www-form-urlencoded");
        return request;
    }

    private AuthenticationHeaderValue BuildBasicAuth()
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    private string MessagingRoot
    {
        get
        {
            var configured = string.IsNullOrWhiteSpace(_settings.BaseUrl)
                ? DefaultMessagingBaseUrl
                : _settings.BaseUrl.TrimEnd('/');
            return configured;
        }
    }

    private string BuildMessagingUrl(string pathAndQuery)
    {
        if (!pathAndQuery.StartsWith('/'))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        return MessagingRoot + pathAndQuery;
    }

    private static string BuildLookupUrl(string pathAndQuery)
    {
        return LookupBaseUrl + pathAndQuery;
    }

    private string? ResolveMessagingNextPage(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return MessagingRoot + absolute.PathAndQuery;
        }

        return BuildMessagingUrl(nextPageUri);
    }

    private static string ToQueryString(IReadOnlyDictionary<string, string> values)
    {
        return string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static (int? ErrorCode, string? Message) TryReadError(TwilioMessageResource? resource, string? json)
    {
        if (resource?.ErrorCode != null)
        {
            return (resource.ErrorCode, resource.ErrorMessage);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null);
        }

        try
        {
            var error = JsonSerializer.Deserialize<TwilioErrorBody>(json, JsonOptions);
            return (error?.Code, error?.Message);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResource resource)
    {
        return new SmsMessageSnapshot
        {
            Sid = resource.Sid,
            Status = resource.Status ?? "unknown",
            ErrorCode = resource.ErrorCode,
            Body = resource.Body,
            DateCreated = ParseTwilioDate(resource.DateCreated),
            DateSent = ParseTwilioDate(resource.DateSent)
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class TwilioLookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("national_format")]
        public string? NationalFormat { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }

    private sealed class TwilioMessageResource
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }

    private sealed class TwilioMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }
    }
}
