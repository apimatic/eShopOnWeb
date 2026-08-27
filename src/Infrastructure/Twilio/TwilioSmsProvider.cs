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

public sealed class TwilioSmsProvider : ISmsProvider, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com/";
    private const string LookupBaseUrl = "https://lookups.twilio.com/";
    private readonly TwilioOptions _options;
    private readonly HttpClient _messagingClient;
    private readonly HttpClient _lookupClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioSmsProvider(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        var authentication = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));

        _messagingClient = CreateClient(authentication);
        _messagingClient.BaseAddress = NormalizeBaseAddress(string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl);

        _lookupClient = CreateClient(authentication);
        _lookupClient.BaseAddress = new Uri(LookupBaseUrl, UriKind.Absolute);
    }

    public async Task<PhoneNumberValidation> ValidateDestinationAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}";
        }

        using var response = await _lookupClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync("phone-number lookup", response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var lookup = await JsonSerializer.DeserializeAsync<LookupResponse>(stream, _jsonOptions, cancellationToken);
        if (lookup is null)
        {
            throw new SmsProviderException("phone-number lookup");
        }

        return new PhoneNumberValidation(
            lookup.Valid,
            lookup.Valid ? lookup.PhoneNumber : null,
            lookup.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<ProviderMessage> SendAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt is not null)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendFormAsync(MessageCollectionPath(), values, "message send", cancellationToken);
    }

    public Task<ProviderMessage> GetAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return GetMessageAsync(MessagePath(providerMessageSid), "message fetch", cancellationToken);
    }

    public Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return SendFormAsync(
            MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "scheduled-message cancellation",
            cancellationToken);
    }

    public Task<ProviderMessage> RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return SendFormAsync(
            MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            "message-content redaction",
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        if (from > to)
        {
            throw new ArgumentException("The start of the reconciliation range must not be after its end.", nameof(from));
        }

        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            // Twilio's list filters have date-only precision and use strict comparison keys.
            // Expand by one UTC day at each edge, then apply the caller's exact timestamps below.
            ["DateSent>"] = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };

        string? nextPath = MessageCollectionPath() + "?" + EncodeQuery(query);
        var messages = new List<ProviderMessage>();

        while (nextPath is not null)
        {
            using var response = await _messagingClient.GetAsync(ToConfiguredBaseRelativePath(nextPath), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync("message reconciliation", response, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<MessageListResponse>(stream, _jsonOptions, cancellationToken)
                ?? throw new SmsProviderException("message reconciliation");

            messages.AddRange(page.Messages.Select(ToProviderMessage));
            nextPath = page.NextPageUri;
        }

        return messages
            .Where(message => (message.SentAt ?? message.CreatedAt) is { } timestamp && timestamp >= from && timestamp <= to)
            .ToList();
    }

    private async Task<ProviderMessage> GetMessageAsync(string path, string operation, CancellationToken cancellationToken)
    {
        using var response = await _messagingClient.GetAsync(path, cancellationToken);
        return await ReadMessageAsync(response, operation, cancellationToken);
    }

    private async Task<ProviderMessage> SendFormAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>> values,
        string operation,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(values);
        using var response = await _messagingClient.PostAsync(path, content, cancellationToken);
        return await ReadMessageAsync(response, operation, cancellationToken);
    }

    private async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync(operation, response, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var message = await JsonSerializer.DeserializeAsync<MessageResponse>(stream, _jsonOptions, cancellationToken)
                ?? throw new SmsProviderException(operation);
            return ToProviderMessage(message);
        }
    }

    private async Task<SmsProviderException> CreateProviderExceptionAsync(
        string operation,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<ErrorResponse>(stream, _jsonOptions, cancellationToken);
            return new SmsProviderException(operation, error?.Code);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new SmsProviderException(operation);
        }
    }

    private static ProviderMessage ToProviderMessage(MessageResponse message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid) || string.IsNullOrWhiteSpace(message.Status))
        {
            throw new SmsProviderException("message response parsing");
        }

        return new ProviderMessage(
            message.Sid,
            message.Status,
            message.ErrorCode,
            ParseProviderDate(message.DateCreated),
            ParseProviderDate(message.DateSent),
            message.Body);
    }

    private string MessageCollectionPath() =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string sid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static HttpClient CreateClient(string authentication)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authentication);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static Uri NormalizeBaseAddress(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address))
        {
            throw new InvalidOperationException("Twilio:BaseUrl must be an absolute URI when configured.");
        }

        return new Uri(address.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static string ToConfiguredBaseRelativePath(string pathOrUri)
    {
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return pathOrUri.TrimStart('/');
    }

    private static string EncodeQuery(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException("Twilio account credentials are not configured.");
        }
    }

    private void EnsureMessagingConfiguration()
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio sender configuration is not configured.");
        }
    }

    public void Dispose()
    {
        _messagingClient.Dispose();
        _lookupClient.Dispose();
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
        [JsonPropertyName("body")]
        public string? Body { get; init; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public MessageResponse[] Messages { get; init; } = Array.Empty<MessageResponse>();
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; init; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }
    }
}
