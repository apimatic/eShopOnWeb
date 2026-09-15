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
/// A deliberately small client for the operations defined by the supplied
/// twilio_lookups_v2 and twilio_api_v2010 OpenAPI documents.
/// </summary>
public sealed class TwilioRestClient : ITwilioLookupClient, ITwilioMessagingClient, IDisposable
{
    private readonly TwilioOptions _options;
    private readonly HttpClient _lookupClient;
    private readonly HttpClient _messagingClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioRestClient(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _lookupClient = CreateClient(TwilioOptions.LookupBaseUrl, _options);
        _messagingClient = CreateClient(_options.MessagingBaseUrl, _options);
    }

    public async Task<TwilioPhoneLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _lookupClient.GetAsync(BuildUri(_lookupClient.BaseAddress!, path), cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);
        EnsureSuccess(response, payload);
        var model = JsonSerializer.Deserialize<LookupResponse>(payload, _jsonOptions)
            ?? throw new TwilioApiException(502, null, "Twilio returned an empty lookup response.");
        return new TwilioPhoneLookup(model.Valid, model.PhoneNumber);
    }

    public async Task<TwilioMessage> CreateAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(sendAt != null);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("From", _options.FromNumber)
        };

        if (sendAt != null)
        {
            fields.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return await PostFormAsync(MessageCollectionPath(), fields, cancellationToken);
    }

    public Task<TwilioMessage> FetchAsync(string messageSid, CancellationToken cancellationToken)
        => GetMessageAsync(MessagePath(messageSid), cancellationToken);

    public Task<TwilioMessage> CancelAsync(string messageSid, CancellationToken cancellationToken)
        => PostFormAsync(MessagePath(messageSid), new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);

    public Task<TwilioMessage> RedactAsync(string messageSid, CancellationToken cancellationToken)
        => PostFormAsync(MessagePath(messageSid), new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);

    public async Task<IReadOnlyList<TwilioMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            // Twilio's date filters are calendar-day based. Pad them so same-day and
            // sub-day ranges cannot lose boundary traffic, then enforce the caller's
            // exact ISO-8601 timestamps on the complete paged result below.
            ["DateSent>"] = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        using var encodedQuery = new FormUrlEncodedContent(query);
        var path = MessageCollectionPath() + "?" + await encodedQuery.ReadAsStringAsync(cancellationToken);
        var messages = new List<TwilioMessage>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrWhiteSpace(path) && visited.Add(path))
        {
            using var response = await _messagingClient.GetAsync(BuildUri(_messagingClient.BaseAddress!, path), cancellationToken);
            var payload = await ReadPayloadAsync(response, cancellationToken);
            EnsureSuccess(response, payload);
            var page = JsonSerializer.Deserialize<MessagePageResponse>(payload, _jsonOptions)
                ?? throw new TwilioApiException(502, null, "Twilio returned an empty message-list response.");
            messages.AddRange((page.Messages ?? Array.Empty<MessageResponse>()).Select(Map));
            path = page.NextPageUri;
        }

        return messages.Where(x =>
        {
            var timestamp = x.DateSent ?? x.DateCreated;
            return timestamp >= from && timestamp <= to;
        }).ToList();
    }

    private async Task<TwilioMessage> GetMessageAsync(string path, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        using var response = await _messagingClient.GetAsync(BuildUri(_messagingClient.BaseAddress!, path), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessage> PostFormAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _messagingClient.PostAsync(BuildUri(_messagingClient.BaseAddress!, path), content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await ReadPayloadAsync(response, cancellationToken);
        EnsureSuccess(response, payload);
        var model = JsonSerializer.Deserialize<MessageResponse>(payload, _jsonOptions)
            ?? throw new TwilioApiException(502, null, "Twilio returned an empty message response.");
        return Map(model);
    }

    private static HttpClient CreateClient(string baseUrl, TwilioOptions options)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException("Twilio base URL must be an absolute URI.");
        }

        var client = new HttpClient(new SocketsHttpHandler()) { BaseAddress = baseAddress };
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static Uri BuildUri(Uri baseAddress, string providerPathOrUri)
    {
        var providerUri = Uri.TryCreate(providerPathOrUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : providerPathOrUri;
        return new Uri($"{baseAddress.AbsoluteUri.TrimEnd('/')}/{providerUri.TrimStart('/')}", UriKind.Absolute);
    }

    private string MessageCollectionPath()
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string messageSid)
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AccountSid and Twilio:AuthToken must be configured.");
        }
    }

    private void EnsureMessagingConfiguration(bool scheduling)
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber must be configured.");
        }

        if (scheduling && string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid must be configured for scheduled messages.");
        }
    }

    private static async Task<string> ReadPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => await response.Content.ReadAsStringAsync(cancellationToken);

    private static void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        TwilioErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<TwilioErrorResponse>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            // Never include a provider response in the exception: it can contain a phone number.
        }

        throw new TwilioApiException(
            (int)response.StatusCode,
            error?.Code,
            error?.Message ?? $"Twilio request failed with HTTP {(int)response.StatusCode}.");
    }

    private static TwilioMessage Map(MessageResponse model)
        => new(
            model.Sid ?? throw new TwilioApiException(502, null, "Twilio response did not contain a message SID."),
            model.From,
            model.To,
            model.Status ?? "unknown",
            model.Body,
            model.ErrorCode,
            ParseDate(model.DateCreated),
            ParseDate(model.DateSent),
            ParseDate(model.DateUpdated));

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;

    public void Dispose()
    {
        _lookupClient.Dispose();
        _messagingClient.Dispose();
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_updated")]
        public string? DateUpdated { get; set; }
    }

    private sealed class MessagePageResponse
    {
        [JsonPropertyName("messages")]
        public MessageResponse[]? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
