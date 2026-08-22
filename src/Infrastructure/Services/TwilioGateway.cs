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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class TwilioGateway : ITwilioGateway
{
    public const string LookupBaseUrl = "https://lookups.twilio.com";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioGateway> _logger;

    public TwilioGateway(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();

        var encoded = Uri.EscapeDataString(phoneNumber);
        var uri = new Uri($"{LookupBaseUrl}/v2/PhoneNumbers/{encoded}?Fields=line_type_intelligence");

        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, uri),
            isIdempotent: true,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Phone lookup failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new HttpRequestException($"Phone lookup failed with HTTP {(int)response.StatusCode}.");
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Phone lookup returned an empty body.");

        return new PhoneLookupResult(
            lookup.Valid,
            lookup.PhoneNumber,
            lookup.NationalFormat,
            lookup.LineTypeIntelligence?.Type,
            lookup.LineTypeIntelligence?.ErrorCode,
            (IReadOnlyList<string>?)lookup.ValidationErrors ?? Array.Empty<string>());
    }

    public async Task<ProviderMessageResult> SendMessageAsync(
        SendProviderMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        EnsureFromNumber();

        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule a message.");
            }

            form["MessagingServiceSid"] = _settings.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            form["From"] = _settings.FromNumber;
        }
        else
        {
            form["From"] = _settings.FromNumber;
        }

        var uri = MessagingUri($"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json");
        using var response = await SendWithRetryAsync(
            () => CreateFormRequest(HttpMethod.Post, uri, form),
            isIdempotent: false,
            cancellationToken);

        return await ReadMessageResultAsync(response, expectCreated: true, cancellationToken);
    }

    public async Task<ProviderMessageResult> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        var uri = MessagingUri($"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json");
        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, uri),
            isIdempotent: true,
            cancellationToken);

        return await ReadMessageResultAsync(response, expectCreated: false, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageResult>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials();

        var query = new List<string>
        {
            "From=" + Uri.EscapeDataString(fromNumber),
            "DateSent%3E=" + Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            "DateSent%3C=" + Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            "PageSize=1000"
        };

        var results = new List<ProviderMessageResult>();
        var next = MessagingUri($"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json?{string.Join("&", query)}");

        while (next != null)
        {
            var pageUri = next;
            using var response = await SendWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, pageUri),
                isIdempotent: true,
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("List messages failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new HttpRequestException($"List messages failed with HTTP {(int)response.StatusCode}.");
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions)
                ?? new TwilioMessageListResponse();

            if (page.Messages != null)
            {
                results.AddRange(page.Messages.Select(MapMessage));
            }

            next = string.IsNullOrWhiteSpace(page.NextPageUri)
                ? null
                : ResolveMessagingUri(page.NextPageUri);
        }

        return results;
    }

    public Task<ProviderMessageResult> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public Task<ProviderMessageResult> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    private async Task<ProviderMessageResult> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var uri = MessagingUri($"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json");
        using var response = await SendWithRetryAsync(
            () => CreateFormRequest(HttpMethod.Post, uri, form),
            isIdempotent: true,
            cancellationToken);

        return await ReadMessageResultAsync(response, expectCreated: false, cancellationToken);
    }

    private async Task<ProviderMessageResult> ReadMessageResultAsync(
        HttpResponseMessage response,
        bool expectCreated,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((expectCreated && response.StatusCode != HttpStatusCode.Created)
            || (!expectCreated && !response.IsSuccessStatusCode))
        {
            var error = TryReadError(payload);
            _logger.LogWarning(
                "Messaging API returned HTTP {StatusCode} with provider code {ErrorCode}.",
                (int)response.StatusCode,
                error?.Code);
            throw new HttpRequestException(
                $"Messaging API returned HTTP {(int)response.StatusCode} (provider code {error?.Code}).");
        }

        var message = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Messaging API returned an empty body.");
        return MapMessage(message);
    }

    private static ProviderMessageResult MapMessage(TwilioMessageResource message)
        => new(
            message.Sid,
            message.Status ?? "unknown",
            message.ErrorCode,
            message.Body,
            message.From,
            message.To,
            ParseRfc2822(message.DateCreated),
            ParseRfc2822(message.DateSent),
            message.MessagingServiceSid);

    private static DateTimeOffset? ParseRfc2822(string? value)
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

    private static TwilioErrorBody? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorBody>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        bool isIdempotent,
        CancellationToken cancellationToken)
    {
        const int maxTries = 5;
        var baseDelay = TimeSpan.FromMilliseconds(500);
        HttpResponseMessage? last = null;

        for (var attempt = 0; attempt < maxTries; attempt++)
        {
            last?.Dispose();
            using var request = requestFactory();
            last = await _httpClient.SendAsync(request, cancellationToken);

            var status = (int)last.StatusCode;
            var retryable = status == 429 || status == 503 || (isIdempotent && status == 500);
            if (!retryable || attempt == maxTries - 1)
            {
                return last;
            }

            var delay = last.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromMilliseconds(
                    Random.Shared.NextDouble() * Math.Min(30_000, baseDelay.TotalMilliseconds * Math.Pow(2, attempt)));

            _logger.LogWarning("Retrying Twilio request after HTTP {StatusCode}; attempt {Attempt}.", status, attempt + 1);
            await Task.Delay(delay, cancellationToken);
        }

        return last!;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = CreateAuthHeader();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private HttpRequestMessage CreateFormRequest(HttpMethod method, Uri uri, IDictionary<string, string> form)
    {
        var request = CreateRequest(method, uri);
        request.Content = new FormUrlEncodedContent(form);
        return request;
    }

    private AuthenticationHeaderValue CreateAuthHeader()
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    private Uri MessagingUri(string relativePath)
        => new(GetMessagingBaseUri(), relativePath);

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        var baseUri = GetMessagingBaseUri();
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            return new Uri(baseUri, absolute.PathAndQuery.TrimStart('/'));
        }

        return new Uri(baseUri, uriOrPath.TrimStart('/'));
    }

    private Uri GetMessagingBaseUri()
    {
        var raw = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');
        return new Uri(raw + "/", UriKind.Absolute);
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio AccountSid and AuthToken are not configured.");
        }
    }

    private void EnsureFromNumber()
    {
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }
    }

    private sealed class TwilioLookupResponse
    {
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
        [JsonPropertyName("line_type_intelligence")] public TwilioLineTypeIntelligence? LineTypeIntelligence { get; set; }
    }

    private sealed class TwilioLineTypeIntelligence
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    }

    private sealed class TwilioMessageListResponse
    {
        [JsonPropertyName("messages")] public List<TwilioMessageResource>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageResource
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("status")] public int? Status { get; set; }
    }
}
