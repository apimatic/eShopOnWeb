using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string rawPhoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var encodedNumber = Uri.EscapeDataString(rawPhoneNumber);
        var relative = $"/v2/PhoneNumbers/{encodedNumber}";
        if (!string.IsNullOrWhiteSpace(countryCode) && !rawPhoneNumber.TrimStart().StartsWith('+'))
        {
            relative += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        var requestUri = new Uri(new Uri(LookupBaseUrl), relative);
        using var response = await SendWithRetryAsync(() => CreateRequest(HttpMethod.Get, requestUri), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookup failed with HTTP {StatusCode} and provider code {ErrorCode}.", (int)response.StatusCode, ReadErrorCode(payload));
            throw new InvalidOperationException("The provider could not look up the phone number.");
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions);
        if (lookup == null)
        {
            return new PhoneNumberLookupResult { IsUsable = false };
        }

        return new PhoneNumberLookupResult
        {
            IsUsable = lookup.Valid,
            CanonicalPhoneNumber = lookup.PhoneNumber,
            NationalFormat = lookup.NationalFormat,
            CountryCode = lookup.CountryCode,
            ValidationErrors = lookup.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>()
        };
    }

    public async Task<ProviderMessageResult> SendAsync(string toE164, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["Body"] = body,
            ["From"] = _options.FromNumber
        };

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                _logger.LogWarning("Cannot schedule a message because Twilio:MessagingServiceSid is not configured.");
                return new ProviderMessageResult { Accepted = false, Status = "failed" };
            }

            form["MessagingServiceSid"] = _options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = sendAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        using var response = await SendWithRetryAsync(
            () => CreateFormRequest(HttpMethod.Post, MessagesCollectionUri(), form),
            cancellationToken,
            retryNonIdempotentOnTooManyRequests: true);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode != HttpStatusCode.Created && !response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Create message failed with HTTP {StatusCode} and provider code {ErrorCode}.", (int)response.StatusCode, ReadErrorCode(payload));
            return new ProviderMessageResult
            {
                Accepted = false,
                Status = "failed",
                ErrorCode = ReadErrorCode(payload)
            };
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        return ToResult(resource, accepted: true);
    }

    public async Task<ProviderMessageResult?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync(() => CreateRequest(HttpMethod.Get, MessageInstanceUri(messageSid)), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetch message failed with HTTP {StatusCode} and provider code {ErrorCode}.", (int)response.StatusCode, ReadErrorCode(payload));
            return null;
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        return ToResult(resource, accepted: true);
    }

    public async Task<ProviderMessageResult?> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await SendWithRetryAsync(
            () => CreateFormRequest(HttpMethod.Post, MessageInstanceUri(messageSid), form),
            cancellationToken,
            retryNonIdempotentOnTooManyRequests: true);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cancel scheduled message failed with HTTP {StatusCode} and provider code {ErrorCode}.", (int)response.StatusCode, ReadErrorCode(payload));
            return null;
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        return ToResult(resource, accepted: true);
    }

    public async Task<ProviderMessageResult?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await SendWithRetryAsync(
            () => CreateFormRequest(HttpMethod.Post, MessageInstanceUri(messageSid), form),
            cancellationToken,
            retryNonIdempotentOnTooManyRequests: true);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Redact message failed with HTTP {StatusCode} and provider code {ErrorCode}.", (int)response.StatusCode, ReadErrorCode(payload));
            return null;
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        return ToResult(resource, accepted: true);
    }

    public async Task<IReadOnlyList<ProviderMessageResult>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessageResult>();
        var fromUtc = from.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        var toUtc = to.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        var firstQuery =
            $"From={Uri.EscapeDataString(_options.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromUtc)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toUtc)}" +
            "&PageSize=1000";

        Uri? pageUri = new Uri(MessagesCollectionUri(), "?" + firstQuery);

        while (pageUri != null)
        {
            var captured = pageUri;
            using var response = await SendWithRetryAsync(() => CreateRequest(HttpMethod.Get, captured), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("List messages failed with HTTP {StatusCode} and provider code {ErrorCode}.", (int)response.StatusCode, ReadErrorCode(payload));
                break;
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToResult(message, accepted: true));
                }
            }

            pageUri = ResolveNextPageUri(page?.NextPageUri);
        }

        return results;
    }

    private Uri MessagingBaseUri
    {
        get
        {
            var configured = _options.BaseUrl;
            if (string.IsNullOrWhiteSpace(configured))
            {
                return new Uri(DefaultMessagingBaseUrl + "/", UriKind.Absolute);
            }

            return new Uri(configured.TrimEnd('/') + "/", UriKind.Absolute);
        }
    }

    private Uri MessagesCollectionUri()
        => new(MessagingBaseUri, $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");

    private Uri MessageInstanceUri(string messageSid)
        => new(MessagingBaseUri, $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");

    private Uri? ResolveNextPageUri(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return new Uri(MessagingBaseUri, absolute.PathAndQuery.TrimStart('/'));
        }

        return new Uri(MessagingBaseUri, nextPageUri.TrimStart('/'));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = BuildBasicAuth();
        return request;
    }

    private HttpRequestMessage CreateFormRequest(HttpMethod method, Uri uri, Dictionary<string, string> form)
    {
        var request = CreateRequest(method, uri);
        request.Content = new FormUrlEncodedContent(form);
        return request;
    }

    private AuthenticationHeaderValue BuildBasicAuth()
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        bool retryNonIdempotentOnTooManyRequests = false)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = requestFactory();
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxAttempts && request.Method == HttpMethod.Get)
            {
                _logger.LogWarning(ex, "Transient failure calling the messaging provider; retrying.");
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)), cancellationToken);
                continue;
            }

            if (response.StatusCode == (HttpStatusCode)429 && attempt < maxAttempts
                && (request.Method == HttpMethod.Get || retryNonIdempotentOnTooManyRequests))
            {
                var delay = ReadRetryAfter(response) ?? TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1));
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < maxAttempts && request.Method == HttpMethod.Get)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)), cancellationToken);
                continue;
            }

            return response;
        }

        throw new InvalidOperationException("The messaging provider request did not complete.");
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private static int? ReadErrorCode(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Provider error bodies are not logged; callers receive a generic failure.
        }

        return null;
    }

    private static ProviderMessageResult ToResult(TwilioMessageResource? resource, bool accepted)
    {
        if (resource == null)
        {
            return new ProviderMessageResult { Accepted = accepted, Status = accepted ? "queued" : "failed" };
        }

        return new ProviderMessageResult
        {
            Accepted = accepted && !string.IsNullOrWhiteSpace(resource.Sid),
            Sid = resource.Sid,
            Status = resource.Status ?? string.Empty,
            ErrorCode = resource.ErrorCode,
            Body = resource.Body,
            From = resource.From,
            To = resource.To,
            DateSent = ParseTwilioDate(resource.DateSent),
            DateCreated = ParseTwilioDate(resource.DateCreated)
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
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("national_format")]
        public string? NationalFormat { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

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

    private sealed class TwilioMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
