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

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class TwilioTextMessageProvider : ITextMessageProvider, IDisposable
{
    private static readonly Uri LookupBaseUri = new("https://lookups.twilio.com/");
    private readonly TwilioOptions _options;
    private readonly HttpClient _messagingClient;
    private readonly HttpClient _lookupClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioTextMessageProvider(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        EnsureConfigured();

        var authentication = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        var messagingBaseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? TwilioOptions.DefaultMessagingBaseUrl
            : _options.BaseUrl;

        _messagingClient = CreateClient(new Uri(AppendSlash(messagingBaseUrl), UriKind.Absolute), authentication);
        _lookupClient = CreateClient(LookupBaseUri, authentication);
    }

    public async Task<ValidatedDestination> ValidateDestinationAsync(string input, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(input)}";
        using var response = await _lookupClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, "number validation", cancellationToken);
        }

        var payload = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        return new ValidatedDestination(
            payload.Valid,
            payload.PhoneNumber,
            payload.ValidationErrors ?? Array.Empty<string>());
    }

    public async Task<ProviderMessage> SendAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new TextMessageProviderException("message scheduling");
            }

            values.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        }

        using var content = new FormUrlEncodedContent(values);
        using var response = await _messagingClient.PostAsync(MessagesPath(), content, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw await CreateExceptionAsync(response, sendAt.HasValue ? "message scheduling" : "message send", cancellationToken);
        }

        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    public async Task<ProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _messagingClient.GetAsync(MessagePath(messageSid), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, "message fetch", cancellationToken);
        }

        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default) =>
        UpdateAsync(messageSid, new KeyValuePair<string, string>("Status", "canceled"), "message cancellation", cancellationToken);

    public Task<ProviderMessage> RedactContentAsync(string messageSid, CancellationToken cancellationToken = default) =>
        UpdateAsync(messageSid, new KeyValuePair<string, string>("Body", string.Empty), "message redaction", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var query = new[]
        {
            Pair("From", _options.FromNumber),
            // The provider's inequality filters operate at UTC-day granularity and exclude
            // the named boundary day. Expand each edge by one day, then enforce the exact
            // caller-supplied instants below.
            Pair("DateSent>", from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Pair("DateSent<", to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Pair("PageSize", "1000")
        };
        var requestUri = $"{MessagesPath()}?{string.Join("&", query)}";
        var messages = new List<ProviderMessage>();

        while (requestUri is not null)
        {
            using var response = await _messagingClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(response, "message reconciliation", cancellationToken);
            }

            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            messages.AddRange(page.Messages.Select(Map));
            requestUri = NormalizeMessagingPath(page.NextPageUri);
        }

        return messages
            .Where(x => x.DateSent.HasValue && x.DateSent.Value >= from && x.DateSent.Value <= to)
            .ToList();
    }

    public void Dispose()
    {
        _messagingClient.Dispose();
        _lookupClient.Dispose();
    }

    private async Task<ProviderMessage> UpdateAsync(
        string messageSid,
        KeyValuePair<string, string> value,
        string operation,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new[] { value });
        using var response = await _messagingClient.PostAsync(MessagePath(messageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, operation, cancellationToken);
        }

        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private static HttpClient CreateClient(Uri baseAddress, string authentication)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authentication);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken)
            ?? throw new TextMessageProviderException("response parsing", httpStatusCode: (int)response.StatusCode);
    }

    private async Task<TextMessageProviderException> CreateExceptionAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        int? code = null;
        try
        {
            var payload = await DeserializeAsync<ErrorResponse>(response, cancellationToken);
            code = payload.Code;
        }
        catch (JsonException)
        {
            // Provider error bodies are not included because they can contain phone numbers.
        }

        return new TextMessageProviderException(operation, code, (int)response.StatusCode);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new InvalidOperationException("Twilio configuration is incomplete.");
        }
    }

    private string MessagesPath() => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string sid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static string Pair(string key, string value) =>
        $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

    private static string AppendSlash(string value) => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";

    private static string? NormalizeMessagingPath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return nextPageUri.TrimStart('/');
    }

    private static ProviderMessage Map(MessageResponse message) => new(
        message.Sid ?? throw new TextMessageProviderException("response parsing"),
        message.Status ?? "unknown",
        message.ErrorCode,
        ParseDate(message.DateCreated),
        ParseDate(message.DateSent),
        message.To);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result.ToUniversalTime()
            : null;

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; init; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; init; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; init; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; init; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; init; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; init; }

        [JsonPropertyName("to")]
        public string? To { get; init; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageResponse> Messages { get; init; } = new();

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; init; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }
    }
}
