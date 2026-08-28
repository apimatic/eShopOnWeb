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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioMessagingClient : ITwilioMessagingClient, IDisposable
{
    private static readonly Uri DefaultMessagingBaseUri = new("https://api.twilio.com/");
    private static readonly Uri LookupBaseUri = new("https://lookups.twilio.com/");
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly AuthenticationHeaderValue _authorization;
    private readonly Uri _messagingBaseUri;

    public TwilioMessagingClient(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _options.EnsureConfigured();
        _messagingBaseUri = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUri
            : new Uri(EnsureTrailingSlash(_options.BaseUrl), UriKind.Absolute);

        // This client intentionally bypasses HttpClientFactory's request logging because Lookup
        // places the shopper's phone number in the URL path.
        _httpClient = new HttpClient(new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}";
        }

        using var request = CreateRequest(HttpMethod.Get, new Uri(LookupBaseUri, path));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response, payload);
        }

        var result = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions)
            ?? throw new InvalidOperationException("The phone-number validation response was empty.");
        return new ValidatedPhoneNumber(result.Valid, result.PhoneNumber, result.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<ProviderMessage> SendMessageAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendFormForMessageAsync(
            HttpMethod.Post,
            MessagingUri($"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json"),
            values,
            cancellationToken);
    }

    public Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        return SendForMessageAsync(
            HttpMethod.Get,
            MessageUri(messageSid),
            cancellationToken);
    }

    public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        return SendFormForMessageAsync(
            HttpMethod.Post,
            MessageUri(messageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            cancellationToken);
    }

    public Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        return SendFormForMessageAsync(
            HttpMethod.Post,
            MessageUri(messageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            // The provider's list API accepts date-only filters. Expand the requested window by
            // one day on each side, then apply the caller's exact ISO-8601 bounds below.
            ["DateSent>"] = fromUtc.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSent<"] = toUtc.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };

        Uri? next = MessagingUri($"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json?{BuildQuery(query)}");
        var messages = new List<ProviderMessage>();

        while (next is not null)
        {
            using var request = CreateRequest(HttpMethod.Get, next);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response, payload);
            }

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions)
                ?? throw new InvalidOperationException("The provider message-list response was empty.");
            messages.AddRange((page.Messages ?? Array.Empty<MessageResponse>()).Select(ToProviderMessage));

            next = string.IsNullOrWhiteSpace(page.NextPageUri)
                ? null
                : RebaseProviderPageUri(page.NextPageUri);
        }

        return messages
            .Where(x => x.DateSent.HasValue && x.DateSent.Value >= from && x.DateSent.Value <= to)
            .ToList();
    }

    private async Task<ProviderMessage> SendForMessageAsync(HttpMethod method, Uri uri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, uri);
        return await SendForMessageAsync(request, cancellationToken);
    }

    private async Task<ProviderMessage> SendFormForMessageAsync(
        HttpMethod method,
        Uri uri,
        IEnumerable<KeyValuePair<string, string>> values,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, uri);
        request.Content = new FormUrlEncodedContent(values);
        return await SendForMessageAsync(request, cancellationToken);
    }

    private async Task<ProviderMessage> SendForMessageAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response, payload);
        }

        var message = JsonSerializer.Deserialize<MessageResponse>(payload, JsonOptions)
            ?? throw new InvalidOperationException("The provider message response was empty.");
        return ToProviderMessage(message);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = _authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Uri MessageUri(string sid) => MessagingUri(
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json");

    private Uri MessagingUri(string relative) => new(_messagingBaseUri, relative.TrimStart('/'));

    private Uri RebaseProviderPageUri(string nextPageUri)
    {
        var parsed = new Uri(nextPageUri, UriKind.RelativeOrAbsolute);
        var relative = parsed.IsAbsoluteUri ? parsed.PathAndQuery.TrimStart('/') : nextPageUri.TrimStart('/');
        var uri = MessagingUri(relative);
        var query = Uri.UnescapeDataString(uri.Query);
        if (!query.Contains($"From={_options.FromNumber}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The provider paging link dropped the required sender filter.");
        }

        return uri;
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string> values) => string.Join(
        "&",
        values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private static ProviderMessage ToProviderMessage(MessageResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Sid) || string.IsNullOrWhiteSpace(response.Status))
        {
            throw new InvalidOperationException("The provider returned a message without a SID or status.");
        }

        return new ProviderMessage(
            response.Sid,
            response.Status,
            response.ErrorCode,
            ParseProviderDate(response.DateCreated),
            ParseProviderDate(response.DateSent));
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }

    private static TwilioApiException CreateApiException(HttpResponseMessage response, string payload)
    {
        int? code = null;
        try
        {
            code = JsonSerializer.Deserialize<ErrorResponse>(payload, JsonOptions)?.Code;
        }
        catch (JsonException)
        {
            // Do not retain or expose the provider body: it can contain a destination number.
        }

        return new TwilioApiException((int)response.StatusCode, code);
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public MessageResponse[]? Messages { get; set; }
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
