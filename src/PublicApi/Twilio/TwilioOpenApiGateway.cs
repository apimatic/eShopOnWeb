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

namespace Microsoft.eShopWeb.PublicApi.Twilio;

/// <summary>
/// A deliberately small client for the operations defined by twilio_api_v2010.yaml
/// and twilio_lookups_v2.yaml. It keeps the checked-in OpenAPI documents as the contract
/// without introducing a third-party SDK.
/// </summary>
public sealed class TwilioOpenApiGateway : ITwilioGateway, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly string _messagingBaseUrl;
    private readonly HttpClient _messagingClient;
    private readonly HttpClient _lookupsClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TwilioOpenApiGateway(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _messagingBaseUrl = (_options.BaseUrl ?? DefaultMessagingBaseUrl).TrimEnd('/');
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _messagingClient = CreateClient(basic);
        _lookupsClient = CreateClient(basic);
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(input)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, LookupsBaseUrl + path);
        using var response = await _lookupsClient.SendAsync(request, cancellationToken);
        var payload = await ReadAsync<LookupResponse>(response, cancellationToken);
        return new ValidatedPhoneNumber(payload.PhoneNumber ?? string.Empty, payload.Valid);
    }

    public Task<TwilioMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateMessageAsync(new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        }, cancellationToken);

    public Task<TwilioMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateMessageAsync(new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
        }, cancellationToken);

    public Task<TwilioMessage> FetchMessageAsync(string sid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Get, MessagePath(sid), null, cancellationToken);

    public Task<TwilioMessage> CancelMessageAsync(string sid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Post, MessagePath(sid), new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<TwilioMessage> RedactMessageAsync(string sid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Post, MessagePath(sid), new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("r", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("r", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        string? next = MessagesPath() + "?" + FormQuery(query);
        var messages = new List<TwilioMessage>();

        while (!string.IsNullOrWhiteSpace(next))
        {
            var relative = NormalizeProviderPageUri(next);
            using var request = new HttpRequestMessage(HttpMethod.Get, _messagingBaseUrl + relative);
            using var response = await _messagingClient.SendAsync(request, cancellationToken);
            var page = await ReadAsync<MessageListResponse>(response, cancellationToken);
            messages.AddRange((page.Messages ?? Array.Empty<MessageResponse>()).Select(Map));
            next = page.NextPageUri;
        }

        return messages;
    }

    private Task<TwilioMessage> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Post, MessagesPath(), form, cancellationToken);

    private async Task<TwilioMessage> SendMessageRequestAsync(HttpMethod method, string path,
        Dictionary<string, string>? form, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        using var request = new HttpRequestMessage(method, _messagingBaseUrl + path);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        using var response = await _messagingClient.SendAsync(request, cancellationToken);
        return Map(await ReadAsync<MessageResponse>(response, cancellationToken));
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            int? code = null;
            try
            {
                var error = await JsonSerializer.DeserializeAsync<TwilioError>(
                    await response.Content.ReadAsStreamAsync(cancellationToken), _jsonOptions, cancellationToken);
                code = error?.Code;
            }
            catch (JsonException) { }

            throw new TwilioApiException((int)response.StatusCode, code);
        }

        var value = await JsonSerializer.DeserializeAsync<T>(
            await response.Content.ReadAsStreamAsync(cancellationToken), _jsonOptions, cancellationToken);
        return value ?? throw new TwilioApiException((int)response.StatusCode, null);
    }

    private string MessagesPath() => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string NormalizeProviderPageUri(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return absolute.PathAndQuery;
        return value.StartsWith('/') ? value : "/" + value;
    }

    private static string FormQuery(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private static HttpClient CreateClient(string basic)
    {
        var client = new HttpClient(new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber))
            throw new InvalidOperationException("Twilio account settings are not configured.");
    }

    private static TwilioMessage Map(MessageResponse response) => new(
        response.Sid ?? string.Empty,
        response.Status ?? "unknown",
        response.Body,
        response.From,
        response.To,
        response.ErrorCode,
        ParseDate(response.DateCreated),
        ParseDate(response.DateSent),
        ParseDate(response.DateUpdated));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;

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
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("date_updated")] public string? DateUpdated { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public MessageResponse[]? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
    }
}
