using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneNumberLookupResult(false, null, null, new[] { "NOT_A_NUMBER" });
        }

        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += "?CountryCode=" + Uri.EscapeDataString(countryCode);
        }

        var uri = new Uri(new Uri(LookupBaseUrl), path);
        using var response = await SendWithRetryAsync(() => CreateRequest(HttpMethod.Get, uri), retryOnServerError: true, cancellationToken);
        var payload = await ReadJsonAsync<LookupResponseDto>(response, cancellationToken);

        var errors = (IReadOnlyList<string>)(payload.ValidationErrors ?? new List<string>());
        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.NationalFormat,
            errors);
    }

    public async Task<ProviderMessage> CreateMessageAsync(CreateProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfiguredForSend();

        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule a message.");
            }

            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
            if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
            {
                fields.Add(new("From", _settings.FromNumber));
            }
        }
        else
        {
            fields.Add(new("From", _settings.FromNumber));
        }

        var uri = MessagingUri($"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json");
        using var response = await SendWithRetryAsync(
            () => CreateFormRequest(HttpMethod.Post, uri, fields),
            retryOnServerError: false,
            cancellationToken);
        var payload = await ReadJsonAsync<MessageDto>(response, cancellationToken);
        return ToProviderMessage(payload);
    }

    public Task<ProviderMessage> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
        => UpdateOrFetchAsync(HttpMethod.Get, providerMessageSid, fields: null, cancellationToken);

    public Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
        => UpdateOrFetchAsync(HttpMethod.Post, providerMessageSid, new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);

    public Task<ProviderMessage> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
        => UpdateOrFetchAsync(HttpMethod.Post, providerMessageSid, new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);

    public async Task<ProviderMessagePage> ListMessagesFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        EnsureConfiguredForSend();

        var fromNumber = _settings.FromNumber;
        var collected = new List<ProviderMessage>();
        var query = new StringBuilder();
        query.Append("From=").Append(Uri.EscapeDataString(fromNumber));
        query.Append("&PageSize=1000");
        query.Append("&").Append(Uri.EscapeDataString("DateSent>")).Append('=').Append(Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        query.Append("&").Append(Uri.EscapeDataString("DateSent<")).Append('=').Append(Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));

        Uri? next = MessagingUri($"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json?{query}");

        while (next != null)
        {
            var pageUri = next;
            using var response = await SendWithRetryAsync(() => CreateRequest(HttpMethod.Get, pageUri), retryOnServerError: true, cancellationToken);
            var page = await ReadJsonAsync<MessageListDto>(response, cancellationToken);
            if (page.Messages != null)
            {
                collected.AddRange(page.Messages.Select(ToProviderMessage));
            }

            next = ResolveNextPage(page.NextPageUri);
        }

        return new ProviderMessagePage(fromNumber, collected);
    }

    private async Task<ProviderMessage> UpdateOrFetchAsync(
        HttpMethod method,
        string providerMessageSid,
        IReadOnlyList<KeyValuePair<string, string>>? fields,
        CancellationToken cancellationToken)
    {
        EnsureAccountSid();
        if (string.IsNullOrWhiteSpace(providerMessageSid))
        {
            throw new ArgumentException("A provider message SID is required.", nameof(providerMessageSid));
        }

        var uri = MessagingUri($"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json");
        using var response = await SendWithRetryAsync(
            () => fields == null ? CreateRequest(method, uri) : CreateFormRequest(method, uri, fields),
            retryOnServerError: method == HttpMethod.Get,
            cancellationToken);
        var payload = await ReadJsonAsync<MessageDto>(response, cancellationToken);
        return ToProviderMessage(payload);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, bool retryOnServerError, CancellationToken cancellationToken)
    {
        const int maxTries = 5;
        const int baseMs = 500;
        const int capMs = 30_000;

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < maxTries; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();
            ApplyBasicAuth(request);
            response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var status = (int)response.StatusCode;
            var retryAfter = ReadRetryAfter(response);
            var retryable = status == 429 || status == 503 || (retryOnServerError && status == 500);

            if (!retryable || attempt == maxTries - 1)
            {
                var error = await TryReadErrorAsync(response, cancellationToken);
                var sanitized = Sanitize(error.Message) ?? $"Twilio request failed with HTTP {status}.";
                _logger.LogWarning("Twilio HTTP {Status} error code {ErrorCode}: {Message}", status, error.Code, sanitized);
                throw new TwilioApiException(status, error.Code, sanitized);
            }

            var window = Math.Min(capMs, baseMs * (int)Math.Pow(2, attempt));
            var delayMs = retryAfter ?? Random.Shared.Next(0, window);
            await Task.Delay(delayMs, cancellationToken);
        }

        throw new TwilioApiException(500, null, "Twilio request failed after retries.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri) => new(method, uri);

    private static HttpRequestMessage CreateFormRequest(HttpMethod method, Uri uri, IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(EncodeForm(fields), Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        return request;
    }

    private static string EncodeForm(IReadOnlyList<KeyValuePair<string, string>> fields)
        => string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value ?? string.Empty)}"));

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        EnsureAccountSid();
        if (string.IsNullOrEmpty(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AuthToken is not configured.");
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private Uri MessagingUri(string relativePath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl.TrimEnd('/') + "/";
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return new Uri(new Uri(baseUrl), relativePath);
    }

    private Uri? ResolveNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl.TrimEnd('/');
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return new Uri(new Uri(messagingBase.TrimEnd('/') + "/"), absolute.PathAndQuery.TrimStart('/'));
        }

        return new Uri(new Uri(messagingBase.TrimEnd('/') + "/"), nextPageUri.TrimStart('/'));
    }

    private void EnsureConfiguredForSend()
    {
        EnsureAccountSid();
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }
    }

    private void EnsureAccountSid()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid))
        {
            throw new InvalidOperationException("Twilio:AccountSid is not configured.");
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (value is null)
        {
            throw new TwilioApiException((int)response.StatusCode, null, "Twilio returned an empty JSON body.");
        }

        return value;
    }

    private static async Task<(int? Code, string? Message)> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<TwilioErrorDto>(stream, JsonOptions, cancellationToken);
            return (error?.Code, error?.Message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static int? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return (int)Math.Clamp(delta.TotalMilliseconds, 0, 30_000);
        }

        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds))
        {
            return (int)Math.Clamp(seconds * 1000, 0, 30_000);
        }

        return null;
    }

    private static ProviderMessage ToProviderMessage(MessageDto dto)
        => new(
            dto.Sid ?? string.Empty,
            dto.Status ?? string.Empty,
            dto.Body,
            dto.ErrorCode,
            Sanitize(dto.ErrorMessage),
            dto.From,
            dto.DateSent,
            dto.DateCreated);

    private static string? Sanitize(string? value)
        => PhoneNumberSanitizer.Redact(value);

    private sealed class LookupResponseDto
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }

    private sealed class MessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? From { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class MessageListDto
    {
        public List<MessageDto>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorDto
    {
        public int? Code { get; set; }
        public string? Message { get; set; }
        public int? Status { get; set; }
    }
}
