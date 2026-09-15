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

// Hand-written from api-specs/twilio/twilio_api_v2010 and twilio_lookups_v2.
// Keeping the wire DTOs here makes the supplied OpenAPI documents the contract without a third-party SDK.
public sealed class TwilioGateway : ITwilioGateway, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TwilioOptions _options;
    private readonly HttpClient _messagingClient;
    private readonly HttpClient _lookupsClient;

    public TwilioGateway(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        Require(nameof(_options.AccountSid), _options.AccountSid);
        Require(nameof(_options.AuthToken), _options.AuthToken);
        Require(nameof(_options.FromNumber), _options.FromNumber);
        Require(nameof(_options.MessagingServiceSid), _options.MessagingServiceSid);

        // Constructing these directly avoids framework HTTP logging of Lookups URLs, which contain the number.
        _messagingClient = CreateClient(_options.BaseUrl ?? DefaultMessagingBaseUrl);
        _lookupsClient = CreateClient(LookupsBaseUrl);
    }

    public async Task<PhoneNumberValidation> ValidateMobileNumberAsync(string input, CancellationToken cancellationToken)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(input)}";
        using var response = await _lookupsClient.GetAsync(path, cancellationToken);
        var payload = await ReadAsync<LookupResponse>(response, "lookup", cancellationToken);
        var canonical = payload.PhoneNumber;
        var usable = payload.Valid && !string.IsNullOrWhiteSpace(canonical);
        var reason = usable ? null : "Twilio does not consider the destination a valid assignable phone number.";
        return new PhoneNumberValidation(usable, canonical, reason);
    }

    public async Task<ProviderMessage> SendMessageAsync(string destination, string body,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
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
            values.Add(new("SendAt", sendAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        }

        using var response = await _messagingClient.PostAsync(MessagesPath(),
            new FormUrlEncodedContent(values), cancellationToken);
        return Map(await ReadAsync<MessageResponse>(response, "create message", cancellationToken));
    }

    public async Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        using var response = await _messagingClient.GetAsync(MessagePath(messageSid), cancellationToken);
        return Map(await ReadAsync<MessageResponse>(response, "fetch message", cancellationToken));
    }

    public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        UpdateMessageAsync(messageSid, new KeyValuePair<string, string>("Status", "canceled"), "cancel message", cancellationToken);

    public Task<ProviderMessage> RedactMessageContentAsync(string messageSid, CancellationToken cancellationToken) =>
        UpdateMessageAsync(messageSid, new KeyValuePair<string, string>("Body", string.Empty), "redact message", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var query = EncodeQuery(new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        });
        string? path = $"{MessagesPath()}?{query}";
        var result = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(path))
        {
            using var response = await _messagingClient.GetAsync(ToMessagingRelativePath(path), cancellationToken);
            var page = await ReadAsync<MessageListResponse>(response, "list messages", cancellationToken);
            result.AddRange(page.Messages.Select(Map));
            path = page.NextPageUri;
        }

        return result;
    }

    private async Task<ProviderMessage> UpdateMessageAsync(string sid,
        KeyValuePair<string, string> value, string operation, CancellationToken cancellationToken)
    {
        using var response = await _messagingClient.PostAsync(MessagePath(sid),
            new FormUrlEncodedContent(new[] { value }), cancellationToken);
        return Map(await ReadAsync<MessageResponse>(response, operation, cancellationToken));
    }

    private HttpClient CreateClient(string baseUrl)
    {
        var client = new HttpClient(new SocketsHttpHandler())
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await JsonSerializer.DeserializeAsync<TwilioError>(stream, JsonOptions, cancellationToken);
            throw new TwilioProviderException(operation, (int)response.StatusCode, error?.Code,
                error?.Message ?? "The provider returned an error without a message.");
        }

        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new TwilioProviderException(operation, (int)response.StatusCode, null, "The provider returned an empty response.");
    }

    private string MessagesPath() => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static string ToMessagingRelativePath(string pathOrUri)
    {
        if (!Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            return pathOrUri.TrimStart('/');
        }
        return (absolute.AbsolutePath + absolute.Query).TrimStart('/');
    }

    private static string EncodeQuery(IReadOnlyDictionary<string, string> values) => string.Join("&",
        values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private static ProviderMessage Map(MessageResponse x) => new(x.Sid ?? string.Empty, x.From, x.To,
        x.Status ?? "unknown", x.Body, ParseDate(x.DateCreated), ParseDate(x.DateSent), x.ErrorCode, x.ErrorMessage);

    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value,
        CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;

    private static void Require(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Twilio:{name} is required.");
    }

    public void Dispose()
    {
        _messagingClient.Dispose();
        _lookupsClient.Dispose();
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("valid")] public bool Valid { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
