using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

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

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber,
        string? countryCode, CancellationToken cancellationToken)
    {
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            uri += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}";
        }

        using var request = CreateRequest(HttpMethod.Get, new Uri(uri));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync<LookupResponse>(response, cancellationToken);

        return new ValidatedPhoneNumber(payload.Valid,
            payload.PhoneNumber,
            payload.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = body
        };

        if (sendAt.HasValue)
        {
            values["ScheduleType"] = "fixed";
            values["SendAt"] = sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        return SendMessageRequestAsync(HttpMethod.Post,
            MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json"),
            values, cancellationToken);
    }

    public Task<ProviderMessage> FetchMessageAsync(string sid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Get, MessageUri(sid), null, cancellationToken);

    public Task<ProviderMessage> CancelMessageAsync(string sid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Post, MessageUri(sid),
            new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<ProviderMessage> RedactMessageAsync(string sid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Post, MessageUri(sid),
            new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            // The provider supports date-only, strict inequality filters. Widen by a day at
            // each edge, then apply the caller's exact instants in the service layer.
            ["DateSent>"] = from.UtcDateTime.Date.AddDays(-1)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.Date.AddDays(1)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        var queryString = string.Join("&", query.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var next = MessagingUri(
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json?{queryString}");
        var messages = new List<ProviderMessage>();

        while (next is not null)
        {
            using var request = CreateRequest(HttpMethod.Get, next);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var page = await ReadPayloadAsync<MessageListResponse>(response, cancellationToken);
            messages.AddRange((page.Messages ?? Array.Empty<MessageResponse>()).Select(Map));
            next = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : MessagingUriFromPage(page.NextPageUri);
        }

        return messages;
    }

    private async Task<ProviderMessage> SendMessageRequestAsync(HttpMethod method, Uri uri,
        IReadOnlyDictionary<string, string>? form, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, uri);
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync<MessageResponse>(response, cancellationToken);
        return Map(payload);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = _authorization;
        return request;
    }

    private async Task<T> ReadPayloadAsync<T>(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            int? code = null;
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(_jsonOptions,
                    cancellationToken);
                code = error?.Code;
            }
            catch (JsonException)
            {
                // Deliberately do not retain or log the provider body; it can contain PII.
            }

            throw new TwilioRequestException(response.StatusCode, code);
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        return payload ?? throw new TwilioRequestException(response.StatusCode, null);
    }

    private Uri MessageUri(string sid) => MessagingUri(
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json");

    private Uri MessagingUriFromPage(string nextPageUri)
    {
        var parsed = new Uri(nextPageUri, UriKind.RelativeOrAbsolute);
        return MessagingUri(parsed.IsAbsoluteUri ? parsed.PathAndQuery : nextPageUri);
    }

    private Uri MessagingUri(string pathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl;
        return new Uri($"{baseUrl!.TrimEnd('/')}/{pathAndQuery.TrimStart('/')}", UriKind.Absolute);
    }

    private static ProviderMessage Map(MessageResponse message) => new(
        message.Sid ?? string.Empty,
        message.Status ?? "unknown",
        message.From,
        message.To,
        message.Body,
        message.ErrorCode,
        ParseProviderDate(message.DateCreated),
        ParseProviderDate(message.DateSent));

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber) ||
            string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("The Twilio configuration section is incomplete.");
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; init; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; init; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; init; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("from")]
        public string? From { get; init; }

        [JsonPropertyName("to")]
        public string? To { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

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

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }
    }
}
