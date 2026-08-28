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

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioSmsProvider : ISmsProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TwilioOptions _options;
    private readonly HttpClient _messagingClient;
    private readonly HttpClient _lookupClient;
    private readonly Uri _messagingBaseUrl;

    public TwilioSmsProvider(IOptions<TwilioOptions> options)
        : this(options.Value, null)
    {
    }

    internal TwilioSmsProvider(TwilioOptions options, HttpMessageHandler? handler)
    {
        _options = options;

        _messagingBaseUrl = new Uri(
            string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://api.twilio.com" : options.BaseUrl,
            UriKind.Absolute);

        _messagingClient = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);
        _lookupClient = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        _messagingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _lookupClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri("https://lookups.twilio.com"), path));
        using var response = await SendAsync(_lookupClient, request, "phone-number validation", cancellationToken);
        var payload = await DeserializeAsync<LookupResponse>(response, "phone-number validation", cancellationToken);

        return new PhoneNumberValidationResult(
            payload.Valid && !string.IsNullOrWhiteSpace(payload.PhoneNumber),
            payload.PhoneNumber,
            payload.ValidationErrors ?? Array.Empty<string>());
    }

    public async Task<SmsMessageSnapshot> SendMessageAsync(
        string e164Destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", e164Destination),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            values.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            values.Add(new KeyValuePair<string, string>("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        using var request = CreateMessagingRequest(
            HttpMethod.Post,
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json",
            values);
        using var response = await SendAsync(_messagingClient, request, "message creation", cancellationToken);
        return ToSnapshot(await DeserializeAsync<MessageResponse>(response, "message creation", cancellationToken));
    }

    public Task<SmsMessageSnapshot> GetMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        return SendMessageResourceRequestAsync(HttpMethod.Get, messageSid, null, "message retrieval", cancellationToken);
    }

    public Task<SmsMessageSnapshot> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        return SendMessageResourceRequestAsync(
            HttpMethod.Post,
            messageSid,
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "scheduled-message cancellation",
            cancellationToken);
    }

    public async Task<SmsMessageSnapshot> RedactMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        using var request = CreateMessagingRequest(
            HttpMethod.Post,
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json",
            new[] { new KeyValuePair<string, string>("Body", string.Empty) });
        using var response = await SendAsync(_messagingClient, request, "message-content redaction", cancellationToken);
        var message = await DeserializeAsync<MessageResponse>(response, "message-content redaction", cancellationToken);
        if (!string.IsNullOrEmpty(message.Body))
        {
            throw new SmsProviderException("message-content redaction confirmation");
        }

        return ToSnapshot(message);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            // Twilio's Message list bounds are day-granular. Query a one-day envelope so
            // strict/inclusive boundary behavior cannot omit a same-day ISO-8601 range;
            // the exact timestamps are applied to every returned page below.
            ["DateSent>"] = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };

        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json?" +
                   string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var messages = new List<SmsMessageSnapshot>();
        var visitedPages = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrWhiteSpace(path) && visitedPages.Add(path))
        {
            using var request = CreateMessagingRequest(HttpMethod.Get, path, null);
            using var response = await SendAsync(_messagingClient, request, "message reconciliation", cancellationToken);
            var page = await DeserializeAsync<MessageListResponse>(response, "message reconciliation", cancellationToken);

            foreach (var providerMessage in page.Messages ?? Array.Empty<MessageResponse>())
            {
                var snapshot = ToSnapshot(providerMessage);
                var occurredAt = snapshot.DateSent ?? snapshot.DateCreated;
                if (occurredAt >= from && occurredAt <= to)
                {
                    messages.Add(snapshot);
                }
            }

            path = ToRelativeMessagingPath(page.NextPageUri);
        }

        return messages;
    }

    public void Dispose()
    {
        _messagingClient.Dispose();
        _lookupClient.Dispose();
    }

    private async Task<SmsMessageSnapshot> SendMessageResourceRequestAsync(
        HttpMethod method,
        string messageSid,
        IEnumerable<KeyValuePair<string, string>>? values,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = CreateMessagingRequest(
            method,
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json",
            values);
        using var response = await SendAsync(_messagingClient, request, operation, cancellationToken);
        return ToSnapshot(await DeserializeAsync<MessageResponse>(response, operation, cancellationToken));
    }

    private HttpRequestMessage CreateMessagingRequest(
        HttpMethod method,
        string relativePath,
        IEnumerable<KeyValuePair<string, string>>? values)
    {
        var request = new HttpRequestMessage(method, BuildMessagingUri(relativePath));
        if (values is not null)
        {
            request.Content = new FormUrlEncodedContent(values);
        }

        return request;
    }

    private Uri BuildMessagingUri(string relativePath)
    {
        return new Uri($"{_messagingBaseUrl.AbsoluteUri.TrimEnd('/')}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }

    private static string? ToRelativeMessagingPath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        return Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : nextPageUri;
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SmsProviderException(operation);
        }
        catch (HttpRequestException ex)
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
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            errorCode = JsonSerializer.Deserialize<ErrorResponse>(json, JsonOptions)?.Code;
        }
        catch (JsonException)
        {
            // The response body is intentionally not retained because provider errors can contain PII.
        }

        response.Dispose();
        throw new SmsProviderException(operation, errorCode);
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await JsonSerializer.DeserializeAsync<T>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                JsonOptions,
                cancellationToken);
            return result ?? throw new SmsProviderException(operation);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException(operation, innerException: ex);
        }
    }

    private static SmsMessageSnapshot ToSnapshot(MessageResponse message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid) || string.IsNullOrWhiteSpace(message.Status))
        {
            throw new SmsProviderException("message-response parsing");
        }

        return new SmsMessageSnapshot(
            message.Sid,
            message.Status,
            message.ErrorCode,
            ParseProviderDate(message.DateCreated),
            ParseProviderDate(message.DateUpdated),
            ParseProviderDate(message.DateSent));
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new SmsProviderException("provider authentication configuration");
        }
    }

    private void EnsureMessagingConfiguration()
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new SmsProviderException("messaging configuration");
        }
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; init; }

        [JsonPropertyName("valid")]
        public bool Valid { get; init; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; init; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("sid")]
        public string? Sid { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; init; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; init; }

        [JsonPropertyName("date_updated")]
        public string? DateUpdated { get; init; }

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
