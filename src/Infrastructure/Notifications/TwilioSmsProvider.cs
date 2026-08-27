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

public sealed class TwilioSmsProvider : ISmsProvider, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioSmsProvider(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient(new SocketsHttpHandler());
    }

    public async Task<PhoneNumberValidation> ValidateDestinationAsync(
        string rawNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return new PhoneNumberValidation(false, null, new[] { "NOT_A_NUMBER" });
        }

        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}";
        }

        using var request = CreateRequest(HttpMethod.Get, new Uri(LookupBaseUrl + path));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        await EnsureSuccessAsync(response, payload);

        var result = JsonSerializer.Deserialize<LookupResponse>(payload, _jsonOptions)
            ?? throw new SmsProviderException("The provider returned an empty lookup response.");
        return new PhoneNumberValidation(
            result.Valid && !string.IsNullOrWhiteSpace(result.PhoneNumber),
            result.PhoneNumber,
            result.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<ProviderMessage> SendAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendFormAsync(HttpMethod.Post, MessagesPath(), fields, cancellationToken);
    }

    public Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        => SendFormAsync(HttpMethod.Get, MessagePath(providerMessageSid), null, cancellationToken);

    public Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        => SendFormAsync(
            HttpMethod.Post,
            MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            cancellationToken);

    public Task<ProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        => SendFormAsync(
            HttpMethod.Post,
            MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["PageSize"] = "1000"
        };
        var path = MessagesPath() + "?" + string.Join("&", query.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var messages = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(path))
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUri(path));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            await EnsureSuccessAsync(response, payload);
            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, _jsonOptions)
                ?? throw new SmsProviderException("The provider returned an empty message-list response.");
            messages.AddRange(page.Messages.Select(Map));
            path = page.NextPageUri;
        }

        return messages
            .Where(x => (x.DateSent ?? x.DateCreated) >= from && (x.DateSent ?? x.DateCreated) <= to)
            .ToList();
    }

    private async Task<ProviderMessage> SendFormAsync(
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>>? fields,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = CreateRequest(method, MessagingUri(path));
        if (fields != null)
        {
            request.Content = new FormUrlEncodedContent(fields);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        await EnsureSuccessAsync(response, payload);
        var message = JsonSerializer.Deserialize<MessageResponse>(payload, _jsonOptions)
            ?? throw new SmsProviderException("The provider returned an empty message response.");
        return Map(message);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Uri MessagingUri(string pathOrUri)
    {
        var path = pathOrUri;
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            path = absolute.PathAndQuery;
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl;
        return new Uri(baseUrl!.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute);
    }

    private string MessagesPath()
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string sid)
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber) ||
            string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new SmsProviderException("Twilio is not configured.");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? code = null;
        try
        {
            code = JsonSerializer.Deserialize<ErrorResponse>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Code;
        }
        catch (JsonException)
        {
            // The status code and provider error code are sufficient; never echo a response that may contain PII.
        }

        throw new SmsProviderException($"The provider request failed with HTTP {(int)response.StatusCode}.", code);
    }

    private static ProviderMessage Map(MessageResponse value)
        => new(
            value.Sid ?? string.Empty,
            value.Status ?? "unknown",
            value.Body,
            value.From,
            value.To,
            value.ErrorCode,
            ParseDate(value.DateCreated),
            ParseDate(value.DateSent));

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result.ToUniversalTime()
            : null;

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
        [JsonPropertyName("body")]
        public string? Body { get; set; }
        [JsonPropertyName("from")]
        public string? From { get; set; }
        [JsonPropertyName("to")]
        public string? To { get; set; }
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
        public MessageResponse[] Messages { get; set; } = Array.Empty<MessageResponse>();
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
