using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsProvider : ISmsProvider, IDisposable
{
    private static readonly Uri DefaultMessagingBaseUri = new("https://api.twilio.com/");
    private static readonly Uri LookupBaseUri = new("https://lookups.twilio.com/");

    private readonly TwilioOptions _options;
    private readonly HttpClient _messagingClient;
    private readonly HttpClient _lookupClient;
    private readonly Uri _messagingBaseUri;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioSmsProvider(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        ValidateOptions(_options);
        _messagingBaseUri = BuildBaseUri(_options.BaseUrl);
        _messagingClient = CreateClient();
        _lookupClient = CreateClient();
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneNumberValidation(false, null);
        }

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(LookupBaseUri, path));
        using var response = await SendAsync(_lookupClient, request, "phone-number validation", cancellationToken);
        var payload = await DeserializeAsync<TwilioLookupResponse>(response, "phone-number validation", cancellationToken);

        return new PhoneNumberValidation(payload.Valid && !string.IsNullOrWhiteSpace(payload.PhoneNumber), payload.PhoneNumber);
    }

    public Task<ProviderMessageState> SendAsync(string to, string content, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", content)
        };

        if (sendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendMessageFormAsync(MessageCollectionPath(), fields, "message creation", cancellationToken);
    }

    public async Task<ProviderMessageState> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessagingUri(MessagePath(providerMessageSid)));
        using var response = await SendAsync(_messagingClient, request, "message retrieval", cancellationToken);
        var payload = await DeserializeAsync<TwilioMessageResponse>(response, "message retrieval", cancellationToken);
        return payload.ToState();
    }

    public Task<ProviderMessageState> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        SendMessageFormAsync(
            MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "scheduled-message cancellation",
            cancellationToken);

    public Task<ProviderMessageState> RedactMessageContentAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        SendMessageFormAsync(
            MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            "message-content redaction",
            cancellationToken);

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.ToUniversalTime().AddSeconds(-1).ToString("O", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.ToUniversalTime().AddSeconds(1).ToString("O", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };

        var path = MessageCollectionPath() + "?" + string.Join("&", query.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var records = new List<ProviderMessageRecord>();
        var seenPageUris = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrWhiteSpace(path) && seenPageUris.Add(path))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MessagingUri(path));
            using var response = await SendAsync(_messagingClient, request, "message reconciliation", cancellationToken);
            var page = await DeserializeAsync<TwilioMessageListResponse>(response, "message reconciliation", cancellationToken);

            records.AddRange(page.Messages.Select(x => new ProviderMessageRecord(
                x.Sid,
                x.Status,
                x.ErrorCode,
                x.DateCreated,
                x.DateSent)));

            path = NormalizeNextPagePath(page.NextPageUri);
        }

        return records
            .Where(x => x.DateSent >= from && x.DateSent <= to)
            .ToList();
    }

    private async Task<ProviderMessageState> SendMessageFormAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>> fields,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagingUri(path))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        using var response = await SendAsync(_messagingClient, request, operation, cancellationToken);
        var payload = await DeserializeAsync<TwilioMessageResponse>(response, operation, cancellationToken);
        return payload.ToState();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException(operation, innerException: ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        int? errorCode = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<TwilioErrorResponse>(stream, cancellationToken: cancellationToken);
            errorCode = error?.Code;
        }
        catch (JsonException)
        {
            // Error response bodies are deliberately not retained because they can contain phone numbers.
        }
        finally
        {
            response.Dispose();
        }

        throw new SmsProviderException(operation, errorCode);
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
            return result ?? throw new SmsProviderException(operation);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException(operation, innerException: ex);
        }
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private string MessageCollectionPath() =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string sid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private Uri MessagingUri(string path) => new(_messagingBaseUri, path.TrimStart('/'));

    private static string? NormalizeNextPagePath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        return Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery.TrimStart('/')
            : nextPageUri.TrimStart('/');
    }

    private static Uri BuildBaseUri(string? configuredBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return DefaultMessagingBaseUri;
        }

        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Twilio:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        return new Uri(configuredBaseUrl.EndsWith("/", StringComparison.Ordinal) ? configuredBaseUrl : configuredBaseUrl + "/");
    }

    private static void ValidateOptions(TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) ||
            string.IsNullOrWhiteSpace(options.AuthToken) ||
            string.IsNullOrWhiteSpace(options.FromNumber) ||
            string.IsNullOrWhiteSpace(options.MessagingServiceSid))
        {
            throw new InvalidOperationException(
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid are required.");
        }
    }

    public void Dispose()
    {
        _messagingClient.Dispose();
        _lookupClient.Dispose();
    }

    private sealed class TwilioLookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; init; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; init; }
    }

    private sealed class TwilioMessageResponse
    {
        [JsonPropertyName("sid")]
        public string Sid { get; init; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; init; }

        [JsonPropertyName("date_created")]
        public string? DateCreatedValue { get; init; }

        [JsonPropertyName("date_sent")]
        public string? DateSentValue { get; init; }

        [JsonIgnore]
        public DateTimeOffset? DateCreated => ParseTwilioDate(DateCreatedValue);

        [JsonIgnore]
        public DateTimeOffset? DateSent => ParseTwilioDate(DateSentValue);

        public ProviderMessageState ToState() => new(Sid, Status, ErrorCode, DateCreated, DateSent);

        private static DateTimeOffset? ParseTwilioDate(string? value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
                ? parsed
                : null;
    }

    private sealed class TwilioMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResponse> Messages { get; init; } = new();

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; init; }
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }
    }
}
