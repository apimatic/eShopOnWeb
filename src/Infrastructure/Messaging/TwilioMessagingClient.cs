using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ISmsGateway, IPhoneNumberLookupService
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupClientName = "TwilioLookup";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string LookupBaseUrl = "https://lookups.twilio.com/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TwilioOptions> _options;
    private readonly ILogger<TwilioMessagingClient> _logger;
    private readonly Random _jitter = new();

    public TwilioMessagingClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TwilioOptions> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await SendWithRetryAsync(
            LookupClientName,
            () => new HttpRequestMessage(HttpMethod.Get, path),
            RetryMode.Idempotent,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookup failed with HTTP {StatusCode} provider code {ProviderCode}",
                (int)response.StatusCode, ReadProviderCode(body));
            throw new InvalidOperationException($"Phone number lookup failed with HTTP {(int)response.StatusCode}.");
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(body, JsonOptions);
        return new PhoneNumberLookupResult
        {
            IsValid = lookup?.Valid == true,
            CanonicalNumber = lookup?.PhoneNumber,
            NationalFormat = lookup?.NationalFormat,
            ValidationErrors = (IReadOnlyList<string>?)lookup?.ValidationErrors ?? Array.Empty<string>()
        };
    }

    public Task<SmsOperationResult> SendAsync(SmsSendCommand command, CancellationToken cancellationToken = default)
        => CreateMessageAsync(command, sendAt: null, cancellationToken);

    public Task<SmsOperationResult> ScheduleAsync(SmsSendCommand command, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
        => CreateMessageAsync(command, sendAt, cancellationToken);

    public async Task<SmsOperationResult> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var path = MessageInstancePath(providerMessageSid);
        using var response = await SendWithRetryAsync(
            MessagingClientName,
            () => new HttpRequestMessage(HttpMethod.Get, MessagingUri(path)),
            RetryMode.Idempotent,
            cancellationToken);
        return await ReadMessageResultAsync(response, cancellationToken);
    }

    public async Task<SmsOperationResult> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var path = MessageInstancePath(providerMessageSid);
        using var response = await SendWithRetryAsync(
            MessagingClientName,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, MessagingUri(path));
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Status"] = "canceled"
                });
                return request;
            },
            RetryMode.CreateSafeRetry,
            cancellationToken);
        return await ReadMessageResultAsync(response, cancellationToken);
    }

    public async Task<SmsOperationResult> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var path = MessageInstancePath(providerMessageSid);
        using var response = await SendWithRetryAsync(
            MessagingClientName,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, MessagingUri(path));
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Body"] = string.Empty
                });
                return request;
            },
            RetryMode.CreateSafeRetry,
            cancellationToken);
        return await ReadMessageResultAsync(response, cancellationToken);
    }

    public async Task<SmsListResult> ListFromConfiguredSenderAsync(DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.FromNumber))
        {
            return new SmsListResult { Succeeded = false, Error = "Twilio:FromNumber is not configured." };
        }

        // Cover the whole datetime range: DateSent filters are date-level, so request a
        // one-day pad on each side and keep the provider's From filter as the only sender cut.
        var startDate = fromInclusive.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endDate = toInclusive.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = new Dictionary<string, string>
        {
            ["From"] = options.FromNumber,
            ["DateSent>"] = startDate,
            ["DateSent<"] = endDate,
            ["PageSize"] = "1000"
        };

        var messages = new List<SmsMessageSnapshot>();
        var next = MessagingUri(MessagesCollectionPath() + "?" + ToQueryString(query));
        var pages = 0;

        while (next != null && pages < 100)
        {
            pages++;
            var target = next;
            using var response = await SendWithRetryAsync(
                MessagingClientName,
                () => new HttpRequestMessage(HttpMethod.Get, target),
                RetryMode.Idempotent,
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("List messages failed with HTTP {StatusCode} provider code {ProviderCode}",
                    (int)response.StatusCode, ReadProviderCode(payload));
                return new SmsListResult
                {
                    Succeeded = false,
                    FromNumber = options.FromNumber,
                    Error = $"Listing messages failed with HTTP {(int)response.StatusCode}."
                };
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions);
            if (page?.Messages != null)
            {
                foreach (var resource in page.Messages)
                {
                    var snapshot = ToSnapshot(resource);
                    if (snapshot == null)
                    {
                        continue;
                    }

                    var when = snapshot.DateSent ?? snapshot.DateCreated;
                    if (when.HasValue && (when < fromInclusive || when > toInclusive))
                    {
                        continue;
                    }

                    messages.Add(snapshot);
                }
            }

            next = ResolveNextPage(page?.NextPageUri);
        }

        return new SmsListResult
        {
            Succeeded = true,
            FromNumber = options.FromNumber,
            Messages = messages
        };
    }

    private async Task<SmsOperationResult> CreateMessageAsync(SmsSendCommand command, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.FromNumber))
        {
            return new SmsOperationResult { Succeeded = false, Error = "Twilio messaging is not configured." };
        }

        if (sendAt.HasValue && string.IsNullOrWhiteSpace(options.MessagingServiceSid))
        {
            return new SmsOperationResult { Succeeded = false, Error = "Twilio:MessagingServiceSid is required to schedule a message." };
        }

        using var response = await SendWithRetryAsync(
            MessagingClientName,
            () =>
            {
                var fields = new Dictionary<string, string>
                {
                    ["To"] = command.To,
                    ["Body"] = command.Body,
                    ["From"] = options.FromNumber
                };

                if (!string.IsNullOrWhiteSpace(options.MessagingServiceSid))
                {
                    fields["MessagingServiceSid"] = options.MessagingServiceSid;
                }

                if (sendAt.HasValue)
                {
                    fields["ScheduleType"] = "fixed";
                    fields["SendAt"] = sendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                }

                var request = new HttpRequestMessage(HttpMethod.Post, MessagingUri(MessagesCollectionPath()));
                request.Content = new FormUrlEncodedContent(fields);
                return request;
            },
            RetryMode.CreateSafeRetry,
            cancellationToken);

        return await ReadMessageResultAsync(response, cancellationToken);
    }

    private async Task<SmsOperationResult> ReadMessageResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Messaging API failed with HTTP {StatusCode} provider code {ProviderCode}",
                (int)response.StatusCode, ReadProviderCode(payload));
            return new SmsOperationResult
            {
                Succeeded = false,
                Error = $"Messaging API failed with HTTP {(int)response.StatusCode}."
            };
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        var snapshot = ToSnapshot(resource);
        if (snapshot == null)
        {
            return new SmsOperationResult { Succeeded = false, Error = "The provider returned a message without a SID." };
        }

        return new SmsOperationResult { Succeeded = true, Message = snapshot };
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string clientName,
        Func<HttpRequestMessage> createRequest,
        RetryMode retryMode,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? last = null;
        const int maxAttempts = 5;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            last?.Dispose();
            using var request = createRequest();
            ApplyAuth(request);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var client = _httpClientFactory.CreateClient(clientName);
            last = await client.SendAsync(request, cancellationToken);

            if (last.IsSuccessStatusCode)
            {
                return last;
            }

            var retryable = last.StatusCode == (HttpStatusCode)429
                || (retryMode == RetryMode.Idempotent && (int)last.StatusCode >= 500);
            if (!retryable || attempt == maxAttempts - 1)
            {
                return last;
            }

            await DelayBeforeRetryAsync(last, attempt, cancellationToken);
        }

        return last!;
    }

    private async Task DelayBeforeRetryAsync(HttpResponseMessage response, int attempt, CancellationToken cancellationToken)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
        {
            await Task.Delay(retryAfter, cancellationToken);
            return;
        }

        var windowMs = Math.Min(30_000, 500 * Math.Pow(2, attempt));
        var delay = TimeSpan.FromMilliseconds(_jitter.NextDouble() * windowMs);
        await Task.Delay(delay, cancellationToken);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var options = _options.CurrentValue;
        var raw = Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    private Uri MessagingUri(string relativeOrAbsolute)
    {
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute))
        {
            return RewriteIfOverridden(absolute);
        }

        return new Uri(GetMessagingBase(), relativeOrAbsolute.TrimStart('/'));
    }

    private Uri? ResolveNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        return MessagingUri(nextPageUri);
    }

    private Uri RewriteIfOverridden(Uri uri)
    {
        var configured = _options.CurrentValue.BaseUrl;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return uri;
        }

        if (!uri.Host.Equals("api.twilio.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        return new Uri(GetMessagingBase(), uri.PathAndQuery.TrimStart('/'));
    }

    private Uri GetMessagingBase()
    {
        var raw = string.IsNullOrWhiteSpace(_options.CurrentValue.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.CurrentValue.BaseUrl.Trim();
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw);
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_options.CurrentValue.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{_options.CurrentValue.AccountSid}/Messages/{sid}.json";

    private static string ToQueryString(Dictionary<string, string> values)
    {
        var parts = new List<string>(values.Count);
        foreach (var pair in values)
        {
            parts.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        }

        return string.Join("&", parts);
    }

    private static SmsMessageSnapshot? ToSnapshot(TwilioMessageResource? resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Sid))
        {
            return null;
        }

        return new SmsMessageSnapshot
        {
            Sid = resource.Sid,
            Status = resource.Status ?? "unknown",
            Body = resource.Body,
            From = resource.From,
            DateSent = ParseTwilioDate(resource.DateSent),
            DateCreated = ParseTwilioDate(resource.DateCreated),
            ErrorCode = resource.ErrorCode,
            ErrorMessage = PhoneNumberSanitizer.Redact(resource.ErrorMessage)
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null")
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ReadProviderCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("code", out var code))
            {
                return code.ToString();
            }
        }
        catch (JsonException)
        {
            // The error body is not JSON; do not log it — it may contain a destination number.
        }

        return "unknown";
    }

    private enum RetryMode
    {
        Idempotent,
        CreateSafeRetry
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

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }

    private sealed class TwilioMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
