using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioApiClient : ITwilioApiClient
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupClientName = "TwilioLookup";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;

    public TwilioApiClient(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber.Trim());
        var url = $"{LookupBaseUrl.TrimEnd('/')}/v2/PhoneNumbers/{encoded}";
        using var response = await SendAsync(LookupClientName, HttpMethod.Get, url, content: null, cancellationToken);
        var json = await ReadJsonAsync<TwilioLookupJson>(response, cancellationToken);

        var errors = json.ValidationErrors ?? new List<string>();
        return new PhoneNumberLookupResult(json.Valid, json.PhoneNumber, errors);
    }

    public async Task<TwilioMessageSnapshot> SendMessageAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", _settings.FromNumber),
            new("Body", request.Body)
        };

        if (request.SendAt.HasValue)
        {
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>("SendAt",
                request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await SendAsync(
            MessagingClientName,
            HttpMethod.Post,
            MessagingUrl($"Accounts/{_settings.AccountSid}/Messages.json"),
            content,
            cancellationToken);

        var json = await ReadJsonAsync<TwilioMessageJson>(response, cancellationToken);
        return ToSnapshot(json);
    }

    public async Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            MessagingClientName,
            HttpMethod.Get,
            MessagingUrl($"Accounts/{_settings.AccountSid}/Messages/{messageSid}.json"),
            content: null,
            cancellationToken);
        var json = await ReadJsonAsync<TwilioMessageJson>(response, cancellationToken);
        return ToSnapshot(json);
    }

    public Task<TwilioMessageSnapshot> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var firstUrl = MessagingUrl(
            $"Accounts/{_settings.AccountSid}/Messages.json?From={Uri.EscapeDataString(fromNumber)}&DateSent>={Uri.EscapeDataString(fromIso)}&DateSent<={Uri.EscapeDataString(toIso)}&PageSize=1000");

        var results = new List<TwilioMessageSnapshot>();
        var nextUrl = firstUrl;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var response = await SendAsync(MessagingClientName, HttpMethod.Get, nextUrl, content: null, cancellationToken);
            var page = await ReadJsonAsync<TwilioMessageListJson>(response, cancellationToken);
            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            nextUrl = ResolveNextPageUrl(page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot> UpdateMessageAsync(
        string messageSid,
        IDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await SendAsync(
            MessagingClientName,
            HttpMethod.Post,
            MessagingUrl($"Accounts/{_settings.AccountSid}/Messages/{messageSid}.json"),
            content,
            cancellationToken);
        var json = await ReadJsonAsync<TwilioMessageJson>(response, cancellationToken);
        return ToSnapshot(json);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string clientName,
        HttpMethod method,
        string url,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var client = _httpClientFactory.CreateClient(clientName);
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicToken());

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var sanitized = PhoneNumberLogSanitizer.Redact(errorBody);
            throw new HttpRequestException(
                $"Twilio request failed with {(int)response.StatusCode} {response.StatusCode}: {sanitized}");
        }

        return response;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (value is null)
        {
            throw new HttpRequestException("Twilio returned an empty response.");
        }

        return value;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio AccountSid and AuthToken are not configured.");
        }
    }

    private string BuildBasicToken()
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
    }

    private string MessagingUrl(string relativePathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.Trim();
        return $"{baseUrl.TrimEnd('/')}/2010-04-01/{relativePathAndQuery.TrimStart('/')}";
    }

    private string? ResolveNextPageUrl(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return MessagingUrl(absolute.PathAndQuery.Replace("/2010-04-01/", string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        var relative = nextPageUri;
        const string prefix = "/2010-04-01/";
        if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[prefix.Length..];
        }

        return MessagingUrl(relative);
    }

    private static TwilioMessageSnapshot ToSnapshot(TwilioMessageJson json)
    {
        return new TwilioMessageSnapshot(
            json.Sid ?? string.Empty,
            json.Status ?? string.Empty,
            json.Body,
            json.From,
            json.To,
            ParseTwilioDate(json.DateSent),
            ParseTwilioDate(json.DateCreated),
            ParseErrorCode(json.ErrorCode),
            json.ErrorMessage);
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

    private static int? ParseErrorCode(JsonElement errorCode)
    {
        if (errorCode.ValueKind == JsonValueKind.Number && errorCode.TryGetInt32(out var numeric))
        {
            return numeric;
        }

        if (errorCode.ValueKind == JsonValueKind.String &&
            int.TryParse(errorCode.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromString))
        {
            return fromString;
        }

        return null;
    }

    private sealed class TwilioLookupJson
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }

    private sealed class TwilioMessageListJson
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageJson>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageJson
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

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

        [JsonPropertyName("error_code")]
        public JsonElement ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }
}
