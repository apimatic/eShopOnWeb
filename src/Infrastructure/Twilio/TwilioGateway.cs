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

/// <summary>
/// A deliberately small client implemented from api-specs/twilio/twilio_api_v2010
/// and api-specs/twilio/twilio_lookups_v2. No Twilio SDK is used.
/// </summary>
public sealed class TwilioGateway : ITwilioGateway, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioGateway(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        });
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));
    }

    public async Task<PhoneNumberLookup> LookupPhoneNumberAsync(string phoneNumber, string? countryCode,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var query = string.IsNullOrWhiteSpace(countryCode)
            ? string.Empty
            : $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}{query}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateProviderExceptionAsync("phone-number lookup", response, cancellationToken);

        var result = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        return new PhoneNumberLookup(result.Valid, result.PhoneNumber);
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string content, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", Required(_options.FromNumber, "FromNumber")),
            new("Body", content)
        };

        if (sendAt is not null)
        {
            values.Add(new("MessagingServiceSid", Required(_options.MessagingServiceSid, "MessagingServiceSid")));
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return PostMessageAsync(values, cancellationToken);
    }

    public Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageResourceRequestAsync(HttpMethod.Get, messageSid, null, "message fetch", cancellationToken);

    public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageResourceRequestAsync(HttpMethod.Post, messageSid,
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Status", "canceled") }),
            "message cancellation", cancellationToken);

    public Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageResourceRequestAsync(HttpMethod.Post, messageSid,
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Body", string.Empty) }),
            "message content redaction", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var query = QueryString(new Dictionary<string, string>
        {
            ["From"] = Required(_options.FromNumber, "FromNumber"),
            ["DateSent>"] = from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        });
        string? resource = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json?{query}";
        var messages = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(resource))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MessagingUri(resource));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw await CreateProviderExceptionAsync("message reconciliation", response, cancellationToken);

            var page = await DeserializeAsync<MessagePageResponse>(response, cancellationToken);
            messages.AddRange(page.Messages.Select(ToProviderMessage));
            resource = page.NextPageUri;
        }

        return messages;
    }

    private async Task<ProviderMessage> PostMessageAsync(IEnumerable<KeyValuePair<string, string>> values,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var resource = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
        using var content = new FormUrlEncodedContent(values);
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagingUri(resource)) { Content = content };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateProviderExceptionAsync("message creation", response, cancellationToken);

        return ToProviderMessage(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private async Task<ProviderMessage> SendMessageResourceRequestAsync(HttpMethod method, string messageSid,
        HttpContent? content, string operation, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var resource = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = new HttpRequestMessage(method, MessagingUri(resource)) { Content = content };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateProviderExceptionAsync(operation, response, cancellationToken);

        return ToProviderMessage(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private string MessagingUri(string resource)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl;
        return $"{baseUrl!.TrimEnd('/')}/{resource.TrimStart('/')}";
    }

    private void EnsureCredentials()
    {
        Required(_options.AccountSid, "AccountSid");
        Required(_options.AuthToken, "AuthToken");
    }

    private static string Required(string value, string key) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"Twilio:{key} is required.");

    private static string QueryString(IReadOnlyDictionary<string, string> values) => string.Join("&",
        values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken)
            ?? throw new TwilioProviderException("response parsing");
    }

    private static async Task<TwilioProviderException> CreateProviderExceptionAsync(string operation,
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int? code = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<TwilioError>(stream, cancellationToken: cancellationToken);
            code = error?.Code;
        }
        catch (JsonException) { }
        return new TwilioProviderException(operation, code);
    }

    private static ProviderMessage ToProviderMessage(MessageResponse message) => new(
        message.Sid ?? string.Empty, message.Status ?? "unknown", message.Body, message.From, message.To,
        message.ErrorCode, message.ErrorMessage, ParseDate(message.DateCreated), ParseDate(message.DateSent),
        ParseDate(message.DateUpdated));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed : null;

    public void Dispose() => _httpClient.Dispose();

    private sealed record LookupResponse(
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("phone_number")] string? PhoneNumber);

    private sealed record MessagePageResponse(
        [property: JsonPropertyName("messages")] List<MessageResponse> Messages,
        [property: JsonPropertyName("next_page_uri")] string? NextPageUri);

    private sealed record MessageResponse(
        [property: JsonPropertyName("sid")] string? Sid,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("from")] string? From,
        [property: JsonPropertyName("to")] string? To,
        [property: JsonPropertyName("error_code")] int? ErrorCode,
        [property: JsonPropertyName("error_message")] string? ErrorMessage,
        [property: JsonPropertyName("date_created")] string? DateCreated,
        [property: JsonPropertyName("date_sent")] string? DateSent,
        [property: JsonPropertyName("date_updated")] string? DateUpdated);

    private sealed record TwilioError([property: JsonPropertyName("code")] int? Code);
}
