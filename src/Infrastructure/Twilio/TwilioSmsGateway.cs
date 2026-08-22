using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio REST client built against api-specs/twilio (Lookups v2 FetchPhoneNumber and
/// api_v2010 CreateMessage / FetchMessage / ListMessage / UpdateMessage).
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string LookupsBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, Combine(LookupsBaseUrl, path));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookups FetchPhoneNumber failed with HTTP {StatusCode}.", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        var lookup = JsonSerializer.Deserialize<LookupResponseDto>(payload, JsonOptions);
        if (lookup is null)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        var errors = lookup.ValidationErrors ?? new List<string>();
        return new PhoneNumberLookupResult(lookup.Valid, lookup.PhoneNumber, errors);
    }

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (!string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            fields.Add(new("From", _options.FromNumber));
        }

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
        }

        if (request.SendAt is not null)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                _logger.LogWarning("Cannot schedule a message because Twilio:MessagingServiceSid is not configured.");
                return new SmsSendResult(false, null, "failed", null, "Messaging Service SID is required to schedule a message.");
            }

            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }

        var url = Combine(GetMessagingBaseUrl(), $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");
        using var httpRequest = CreateRequest(HttpMethod.Post, url);
        httpRequest.Content = new FormUrlEncodedContent(fields);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(payload);
            _logger.LogWarning("CreateMessage failed with HTTP {StatusCode} provider code {ProviderCode}.",
                (int)response.StatusCode, error?.Code);
            return new SmsSendResult(false, null, "failed", error?.Code, error?.Message ?? "Provider rejected the message.");
        }

        var message = JsonSerializer.Deserialize<MessageDto>(payload, JsonOptions);
        if (message is null || string.IsNullOrEmpty(message.Sid))
        {
            return new SmsSendResult(false, null, "failed", null, "Provider returned an empty message resource.");
        }

        _logger.LogInformation("CreateMessage accepted provider message {MessageSid} with status {Status}.", message.Sid, message.Status);
        return new SmsSendResult(true, message.Sid, message.Status, message.ErrorCode, message.ErrorMessage);
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var url = Combine(GetMessagingBaseUrl(),
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json");
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("FetchMessage {MessageSid} failed with HTTP {StatusCode}.", providerMessageSid, (int)response.StatusCode);
            return null;
        }

        var message = JsonSerializer.Deserialize<MessageDto>(payload, JsonOptions);
        return message is null ? null : ToSnapshot(message);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageSnapshot>();
        var fromIso = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var toIso = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

        var query = new StringBuilder();
        query.Append("From=").Append(Uri.EscapeDataString(fromNumber));
        query.Append('&').Append(Uri.EscapeDataString("DateSent>")).Append('=').Append(Uri.EscapeDataString(fromIso));
        query.Append('&').Append(Uri.EscapeDataString("DateSent<")).Append('=').Append(Uri.EscapeDataString(toIso));
        query.Append("&PageSize=1000");

        var nextUrl = Combine(GetMessagingBaseUrl(),
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json?{query}");

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = CreateRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ListMessage failed with HTTP {StatusCode}.", (int)response.StatusCode);
                break;
            }

            var page = JsonSerializer.Deserialize<ListMessageResponseDto>(payload, JsonOptions);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            nextUrl = ResolveMessagingUri(page?.NextPageUri);
        }

        return results;
    }

    public async Task<SmsMessageSnapshot> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        return await UpdateMessageAsync(providerMessageSid, fields, cancellationToken);
    }

    public async Task<SmsMessageSnapshot> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        return await UpdateMessageAsync(providerMessageSid, fields, cancellationToken);
    }

    private async Task<SmsMessageSnapshot> UpdateMessageAsync(
        string providerMessageSid,
        List<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var url = Combine(GetMessagingBaseUrl(),
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json");
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(payload);
            _logger.LogWarning("UpdateMessage {MessageSid} failed with HTTP {StatusCode} provider code {ProviderCode}.",
                providerMessageSid, (int)response.StatusCode, error?.Code);
            throw new InvalidOperationException("Provider could not update the message.");
        }

        var message = JsonSerializer.Deserialize<MessageDto>(payload, JsonOptions)
                      ?? throw new InvalidOperationException("Provider returned an empty message resource.");
        return ToSnapshot(message);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string GetMessagingBaseUrl()
    {
        return string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl.TrimEnd('/');
    }

    private string? ResolveMessagingUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
        {
            return Combine(GetMessagingBaseUrl(), absolute.PathAndQuery);
        }

        return Combine(GetMessagingBaseUrl(), uri);
    }

    private static string Combine(string baseUrl, string path)
    {
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static SmsMessageSnapshot ToSnapshot(MessageDto message) =>
        new(
            message.Sid ?? string.Empty,
            message.Status,
            message.Body,
            message.From,
            message.To,
            message.DateSent,
            message.DateCreated,
            message.ErrorCode,
            message.ErrorMessage,
            message.Direction);

    private static TwilioErrorDto? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class LookupResponseDto
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }

    private sealed class MessageDto
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
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("direction")]
        public string? Direction { get; set; }
    }

    private sealed class ListMessageResponseDto
    {
        [JsonPropertyName("messages")]
        public List<MessageDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorDto
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }
    }
}
