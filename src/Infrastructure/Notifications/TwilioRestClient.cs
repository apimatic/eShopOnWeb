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

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

// Hand-written from api-specs/twilio/twilio_api_v2010 and twilio_lookups_v2.
// Keeping this client small makes the supplied OpenAPI operations and fields explicit.
public sealed class TwilioRestClient : ITwilioClient, IDisposable
{
    private const string MessagingDefaultBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioRestClient(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        });
    }

    public async Task<TwilioPhoneLookup> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, new Uri(LookupsBaseUrl + path));
        using var response = await SendAsync(request, cancellationToken);
        var payload = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        return new TwilioPhoneLookup(payload.Valid, payload.PhoneNumber);
    }

    public Task<TwilioMessageRecord> SendMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt is not null)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendMessageRequestAsync(HttpMethod.Post, MessagesPath(), fields, cancellationToken);
    }

    public Task<TwilioMessageRecord> FetchMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Get, MessagePath(messageSid), null, cancellationToken);

    public Task<TwilioMessageRecord> CancelMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(
            HttpMethod.Post,
            MessagePath(messageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            cancellationToken);

    public Task<TwilioMessageRecord> RedactMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(
            HttpMethod.Post,
            MessagePath(messageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            cancellationToken);

    public async Task<IReadOnlyList<TwilioMessageRecord>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        string? relativeUri = MessagesPath() + "?" + FormQuery(query);
        var all = new List<TwilioMessageRecord>();

        while (relativeUri is not null)
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUri(relativeUri));
            using var response = await SendAsync(request, cancellationToken);
            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            all.AddRange(page.Messages.Select(ToRecord));
            relativeUri = page.NextPageUri;
        }

        return all;
    }

    private async Task<TwilioMessageRecord> SendMessageRequestAsync(
        HttpMethod method,
        string relativePath,
        IEnumerable<KeyValuePair<string, string>>? form,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        using var request = CreateRequest(method, MessagingUri(relativePath));
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        using var response = await SendAsync(request, cancellationToken);
        var record = ToRecord(await DeserializeAsync<MessageResponse>(response, cancellationToken));
        if (string.IsNullOrWhiteSpace(record.Sid))
            throw new TwilioProviderException((int)response.StatusCode, "Twilio returned a message without an identifier.");
        return record;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                response.Dispose();
                throw new TwilioProviderException(statusCode, $"Twilio returned HTTP {statusCode}.");
            }
            return response;
        }
        catch (TwilioProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TwilioProviderException(null, "The Twilio request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new TwilioProviderException(null, "The Twilio request could not be completed.");
        }
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(content, _jsonOptions, cancellationToken);
        return value ?? throw new TwilioProviderException((int)response.StatusCode, "Twilio returned an empty response.");
    }

    private Uri MessagingUri(string relativePath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? MessagingDefaultBaseUrl : _options.BaseUrl;
        return new Uri(baseUrl!.TrimEnd('/') + "/" + relativePath.TrimStart('/'), UriKind.Absolute);
    }

    private string MessagesPath() => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string messageSid) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private static string FormQuery(IReadOnlyDictionary<string, string> values) => string.Join(
        "&",
        values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
            throw new TwilioProviderException(null, "Twilio credentials are not configured.");
    }

    private void EnsureMessagingConfiguration()
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            throw new TwilioProviderException(null, "Twilio messaging is not configured.");
    }

    private static TwilioMessageRecord ToRecord(MessageResponse value) => new(
        value.Sid ?? string.Empty,
        value.Body,
        value.From,
        value.To,
        value.Status ?? "unknown",
        value.ErrorCode,
        value.ErrorMessage,
        ParseDate(value.DateCreated),
        ParseDate(value.DateSent));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }
        [JsonPropertyName("body")]
        public string? Body { get; set; }
        [JsonPropertyName("from")]
        public string? From { get; set; }
        [JsonPropertyName("to")]
        public string? To { get; set; }
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }
}
