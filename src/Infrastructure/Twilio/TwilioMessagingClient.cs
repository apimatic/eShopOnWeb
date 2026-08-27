using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioMessagingClient : ITwilioMessagingClient, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly AuthenticationHeaderValue _authorization;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioMessagingClient(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        EnsureConfigured();

        // Deliberately not created by IHttpClientFactory: its standard logging handlers include
        // request URLs, while Lookup places the shopper's number in the URL path.
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        var escapedNumber = Uri.EscapeDataString(phoneNumber);
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{escapedNumber}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            uri += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}";
        }

        using var request = CreateRequest(HttpMethod.Get, new Uri(uri));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload, "phone-number validation");

        var result = JsonSerializer.Deserialize<LookupResponse>(payload, _jsonOptions)
            ?? throw new TwilioProviderException("phone-number validation", 502);

        return new ValidatedPhoneNumber(
            result.Valid && !string.IsNullOrWhiteSpace(result.PhoneNumber),
            result.PhoneNumber,
            result.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<ProviderMessage> SendAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
        return SendMessageRequestAsync(HttpMethod.Post, path, values, "message creation", cancellationToken);
    }

    public Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken)
    {
        return SendMessageRequestAsync(
            HttpMethod.Get,
            MessagePath(messageSid),
            null,
            "message retrieval",
            cancellationToken);
    }

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken)
    {
        return SendMessageRequestAsync(
            HttpMethod.Post,
            MessagePath(messageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "scheduled-message cancellation",
            cancellationToken);
    }

    public async Task<ProviderMessage> RedactAsync(string messageSid, CancellationToken cancellationToken)
    {
        var redacted = await SendMessageRequestAsync(
            HttpMethod.Post,
            MessagePath(messageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            "message-content redaction",
            cancellationToken);
        if (!string.IsNullOrEmpty(redacted.Body))
        {
            throw new TwilioProviderException("message-content redaction verification", 502);
        }

        return redacted;
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        // The API's date filters are day-granular. Expand by a day on both sides,
        // then apply the caller's exact ISO-8601 instants after every page is read.
        var fromDate = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_options.FromNumber)}",
            $"DateSent%3E={Uri.EscapeDataString(fromDate)}",
            $"DateSent%3C={Uri.EscapeDataString(toDate)}",
            "PageSize=1000"
        });
        var nextPath = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json?{query}";
        var pagesSeen = new HashSet<string>(StringComparer.Ordinal);
        var messages = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(nextPath) && pagesSeen.Add(nextPath))
        {
            using var request = CreateRequest(HttpMethod.Get, CreateMessagingUri(nextPath));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, payload, "message reconciliation");

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, _jsonOptions)
                ?? throw new TwilioProviderException("message reconciliation", 502);

            messages.AddRange((page.Messages ?? Array.Empty<MessageResponse>()).Select(ToProviderMessage));
            nextPath = NormalizeNextPagePath(page.NextPageUri);
        }

        return messages
            .Where(message =>
            {
                var timestamp = message.DateSent ?? message.DateCreated;
                return timestamp.HasValue && timestamp.Value >= from && timestamp.Value <= to;
            })
            .ToList();
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<ProviderMessage> SendMessageRequestAsync(
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>>? formValues,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, CreateMessagingUri(path));
        if (formValues != null)
        {
            request.Content = new FormUrlEncodedContent(formValues);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload, operation);

        var message = JsonSerializer.Deserialize<MessageResponse>(payload, _jsonOptions)
            ?? throw new TwilioProviderException(operation, 502);
        return ToProviderMessage(message);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = _authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Uri CreateMessagingUri(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl;
        return new Uri($"{baseUrl!.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);
    }

    private string MessagePath(string messageSid) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private static string? NormalizeNextPagePath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        return Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : nextPageUri;
    }

    private static ProviderMessage ToProviderMessage(MessageResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Sid) || string.IsNullOrWhiteSpace(response.Status))
        {
            throw new TwilioProviderException("message response parsing", 502);
        }

        return new ProviderMessage(
            response.Sid,
            response.Status,
            response.ErrorCode,
            ParseProviderDate(response.DateCreated),
            ParseProviderDate(response.DateSent),
            response.Body);
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string payload, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? errorCode = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var parsed))
            {
                errorCode = parsed;
            }
        }
        catch (JsonException)
        {
            // Provider bodies are intentionally not surfaced because they can contain phone numbers.
        }

        throw new TwilioProviderException(operation, (int)response.StatusCode, errorCode);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber) ||
            string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("The Twilio configuration section is incomplete.");
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl) &&
            (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
             (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException("Twilio:BaseUrl must be an absolute HTTP(S) URL.");
        }
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; init; }

        [JsonPropertyName("valid")]
        public bool Valid { get; init; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; init; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("sid")]
        public string? Sid { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; init; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; init; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; init; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public MessageResponse[]? Messages { get; init; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; init; }
    }
}
