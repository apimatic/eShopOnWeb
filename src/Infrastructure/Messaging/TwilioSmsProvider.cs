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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private static readonly TimeSpan RetryBase = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryCap = TimeSpan.FromSeconds(8);
    private const int MaxRetries = 4;
    private const int MaxListPages = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex PhoneLike = new(@"\+\d{8,15}", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var encoded = Uri.EscapeDataString(phoneNumber.Trim());
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{encoded}?Fields=line_type_intelligence";
        using var request = CreateAuthorizedRequest(HttpMethod.Get, url);
        using var response = await SendWithRetryAsync(request, isSend: false, cancellationToken);
        var payload = await ReadJsonAsync<LookupResponseDto>(response, cancellationToken);

        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.NationalFormat,
            payload.LineTypeIntelligence?.Type,
            payload.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>(),
            payload.LineTypeIntelligence?.ErrorCode);
    }

    public async Task<ProviderMessage> SendAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var form = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", _settings.FromNumber),
            new("Body", request.Body),
            new("SmartEncoded", "true")
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            form.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (request.SendAt is not null)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new SmsProviderException("Twilio:MessagingServiceSid is required to schedule a message.", HttpStatusCode.BadRequest);
            }

            form.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            form.Add(new KeyValuePair<string, string>("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var httpRequest = CreateAuthorizedRequest(HttpMethod.Post, MessagesCollectionUrl());
        httpRequest.Content = EncodeForm(form);
        httpRequest.Headers.ExpectContinue = false;
        using var response = await SendWithRetryAsync(httpRequest, isSend: true, cancellationToken);
        var dto = await ReadJsonAsync<TwilioMessageDto>(response, cancellationToken);
        return MapMessage(dto);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, MessageInstanceUrl(messageSid));
        using var response = await SendWithRetryAsync(request, isSend: false, cancellationToken);
        var dto = await ReadJsonAsync<TwilioMessageDto>(response, cancellationToken);
        return MapMessage(dto);
    }

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, status: "canceled", body: null, cancellationToken);
    }

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, status: null, body: string.Empty, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var fromDate = from.UtcDateTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // DateSent< is exclusive of the named day, so pass the day after `to` to keep `to` included.
        var toDateExclusive = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = string.Join("&",
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"DateSent%3E={Uri.EscapeDataString(fromDate)}",
            $"DateSent%3C={Uri.EscapeDataString(toDateExclusive)}",
            "PageSize=1000");

        var results = new List<ProviderMessage>();
        var nextUrl = $"{MessagesCollectionUrl()}?{query}";
        var pages = 0;

        while (!string.IsNullOrEmpty(nextUrl) && pages < MaxListPages)
        {
            pages++;
            using var request = CreateAuthorizedRequest(HttpMethod.Get, nextUrl);
            using var response = await SendWithRetryAsync(request, isSend: false, cancellationToken);
            var page = await ReadJsonAsync<TwilioMessageListDto>(response, cancellationToken);
            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    var mapped = MapMessage(message);
                    if (IsInRange(mapped, from, to))
                    {
                        results.Add(mapped);
                    }
                }
            }

            nextUrl = string.IsNullOrEmpty(page.NextPageUri)
                ? null
                : ResolveMessagingUri(page.NextPageUri);
        }

        return results;
    }

    private async Task<ProviderMessage> UpdateMessageAsync(string messageSid, string? status, string? body, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var form = new List<KeyValuePair<string, string>>();
        if (status is not null)
        {
            form.Add(new KeyValuePair<string, string>("Status", status));
        }

        if (body is not null)
        {
            form.Add(new KeyValuePair<string, string>("Body", body));
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Post, MessageInstanceUrl(messageSid));
        request.Content = EncodeForm(form);
        request.Headers.ExpectContinue = false;
        using var response = await SendWithRetryAsync(request, isSend: false, cancellationToken);
        var dto = await ReadJsonAsync<TwilioMessageDto>(response, cancellationToken);
        return MapMessage(dto);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, bool isSend, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var clone = await CloneAsync(request, cancellationToken);
                request.Dispose();
                request = clone;
            }

            response?.Dispose();
            response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests
                || response.StatusCode == HttpStatusCode.ServiceUnavailable
                || (response.StatusCode == HttpStatusCode.InternalServerError && !isSend);

            if (!retryable || attempt == MaxRetries)
            {
                var error = await TryReadErrorAsync(response, cancellationToken);
                throw new SmsProviderException(FormatError(response.StatusCode, error), response.StatusCode, error?.Code);
            }

            var delay = GetRetryDelay(response, attempt);
            _logger.LogWarning("Retrying Twilio messaging request after {Delay}ms (attempt {Attempt}).", delay.TotalMilliseconds, attempt + 1);
            await Task.Delay(delay, cancellationToken);
        }

        throw new SmsProviderException("Twilio request failed.", HttpStatusCode.InternalServerError);
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            return retryAfter;
        }

        var window = TimeSpan.FromMilliseconds(Math.Min(RetryCap.TotalMilliseconds, RetryBase.TotalMilliseconds * Math.Pow(2, attempt)));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * window.TotalMilliseconds);
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (value is null)
        {
            throw new SmsProviderException("Twilio returned an empty payload.", response.StatusCode);
        }

        return value;
    }

    private static async Task<TwilioErrorDto?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<TwilioErrorDto>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static StringContent EncodeForm(IEnumerable<KeyValuePair<string, string>> form)
    {
        var encoded = string.Join("&", form.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
        return new StringContent(encoded, Encoding.UTF8, "application/x-www-form-urlencoded");
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private string MessagesCollectionUrl()
        => new Uri(MessagingBaseUri(), $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json").ToString();

    private string MessageInstanceUrl(string sid)
        => new Uri(MessagingBaseUri(), $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json").ToString();

    private Uri MessagingBaseUri()
    {
        var configured = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.Trim();
        if (!configured.EndsWith('/'))
        {
            configured += "/";
        }

        return new Uri(configured, UriKind.Absolute);
    }

    private string ResolveMessagingUri(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return new Uri(MessagingBaseUri(), absolute.PathAndQuery).ToString();
            }

            return absolute.ToString();
        }

        return new Uri(MessagingBaseUri(), nextPageUri.TrimStart('/')).ToString();
    }

    private static bool IsInRange(ProviderMessage message, DateTimeOffset from, DateTimeOffset to)
    {
        var stamp = message.DateSent ?? message.DateCreated;
        if (stamp is null)
        {
            return true;
        }

        return stamp >= from && stamp <= to;
    }

    private static ProviderMessage MapMessage(TwilioMessageDto dto)
        => new(
            dto.Sid,
            dto.Status,
            dto.ErrorCode,
            dto.Body,
            dto.From,
            dto.To,
            ParseTwilioDate(dto.DateSent),
            ParseTwilioDate(dto.DateCreated));

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

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new SmsProviderException("Twilio credentials are not configured.", HttpStatusCode.ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new SmsProviderException("Twilio:FromNumber is not configured.", HttpStatusCode.ServiceUnavailable);
        }
    }

    private static string FormatError(HttpStatusCode statusCode, TwilioErrorDto? error)
    {
        var raw = Sanitize(error?.Message ?? $"Twilio request failed with {(int)statusCode}.");
        if (raw.Contains("/Accounts/", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("Messages/", StringComparison.OrdinalIgnoreCase))
        {
            return $"Twilio request failed with {(int)statusCode}.";
        }

        return raw;
    }

    private static string Sanitize(string value) => PhoneLike.Replace(value, "[redacted]");

    private sealed class TwilioMessageDto
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
    }

    private sealed class TwilioMessageListDto
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class LookupResponseDto
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("national_format")]
        public string? NationalFormat { get; set; }

        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }

        [JsonPropertyName("line_type_intelligence")]
        public LineTypeIntelligenceDto? LineTypeIntelligence { get; set; }
    }

    private sealed class LineTypeIntelligenceDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
    }

    private sealed class TwilioErrorDto
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
