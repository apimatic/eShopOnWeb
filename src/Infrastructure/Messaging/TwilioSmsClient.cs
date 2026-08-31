using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of the messaging provider client.
///
/// Messaging API (send, schedule, cancel, redact, fetch, list):
///   {BaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages[.json|/{Sid}.json]
///   BaseUrl defaults to https://api.twilio.com and is overridden verbatim by Twilio:BaseUrl.
/// Lookup API (number validation) is served from https://lookups.twilio.com and is
///   not governed by Twilio:BaseUrl.
/// All calls authenticate with HTTP Basic (AccountSid:AuthToken).
/// Phone numbers and the auth token are never logged.
/// </summary>
public class TwilioSmsClient : ISmsProviderClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsClient> _logger;

    public TwilioSmsClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioSmsClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AccountSid and Twilio:AuthToken must be configured (e.g. via user-secrets).");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl!.TrimEnd('/');

    private string MessagesUrl => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageUrl(string sid) => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Twilio Lookup v2 Basic Lookup: returns the E.164 canonical form and a validity verdict.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult { IsValid = false, ValidationError = "The provider does not recognize this number." };
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await ReadJsonAsync(response, cancellationToken);
        var isValid = payload.TryGetProperty("valid", out var validProp) && validProp.ValueKind == JsonValueKind.True;
        var canonical = payload.TryGetProperty("phone_number", out var numberProp) && numberProp.ValueKind == JsonValueKind.String
            ? numberProp.GetString()
            : null;
        var validationError = payload.TryGetProperty("validation_errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Array
            ? string.Join("; ", errorsProp.EnumerateArray().Select(e => e.GetString()))
            : null;

        return new PhoneNumberValidationResult { IsValid = isValid, CanonicalNumber = canonical, ValidationError = validationError };
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };
        return PostMessageAsync(MessagesUrl, form, cancellationToken);
    }

    public Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduled messages require a Messaging Service; ScheduleType=fixed with an ISO-8601 SendAt.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return PostMessageAsync(MessagesUrl, form, cancellationToken);
    }

    public Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return PostMessageAsync(MessageUrl(providerMessageSid), form, cancellationToken);
    }

    public Task<ProviderMessage> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Updating the body to an empty string redacts the message text at the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        return PostMessageAsync(MessageUrl(providerMessageSid), form, cancellationToken);
    }

    public async Task<ProviderMessage> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await ReadJsonAsync(response, cancellationToken);
        return MapMessage(payload);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only this application's sending number's messages.
        // DateSent filters accept GMT date-times; the range is [from, to).
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(fromNumber)}",
            $"{Uri.EscapeDataString("DateSent>=")}={Uri.EscapeDataString(FormatTwilioDate(from))}",
            $"{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(FormatTwilioDate(to))}",
            "PageSize=1000"
        });

        var messages = new List<ProviderMessage>();
        string? nextUrl = $"{MessagesUrl}?{query}";

        while (nextUrl != null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var payload = await ReadJsonAsync(response, cancellationToken);

            if (payload.TryGetProperty("messages", out var messagesProp) && messagesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messagesProp.EnumerateArray())
                {
                    messages.Add(MapMessage(message));
                }
            }

            nextUrl = payload.TryGetProperty("next_page_uri", out var nextProp) && nextProp.ValueKind == JsonValueKind.String
                ? new Uri(new Uri(MessagingBaseUrl), nextProp.GetString()).ToString()
                : null;
        }

        return messages;
    }

    private static string FormatTwilioDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private async Task<ProviderMessage> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await ReadJsonAsync(response, cancellationToken);
        return MapMessage(payload);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? errorCode = null;
        var message = $"Twilio request failed with status {(int)response.StatusCode}.";
        try
        {
            var payload = await ReadJsonAsync(response, cancellationToken);
            if (payload.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number)
            {
                errorCode = codeProp.GetInt32();
            }
            if (payload.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
            {
                message = messageProp.GetString() ?? message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse a Twilio error response (HTTP {StatusCode}).", (int)response.StatusCode);
        }

        _logger.LogError("Twilio request failed: HTTP {StatusCode}, provider error code {ErrorCode}.", (int)response.StatusCode, errorCode);
        throw new SmsProviderException(message, errorCode);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private static ProviderMessage MapMessage(JsonElement payload)
    {
        return new ProviderMessage
        {
            Sid = GetString(payload, "sid") ?? string.Empty,
            Status = GetString(payload, "status") ?? string.Empty,
            To = GetString(payload, "to"),
            From = GetString(payload, "from"),
            Body = GetString(payload, "body"),
            DateCreated = GetDate(payload, "date_created"),
            DateSent = GetDate(payload, "date_sent"),
            ErrorCode = payload.TryGetProperty("error_code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number
                ? codeProp.GetInt32()
                : null,
            ErrorMessage = GetString(payload, "error_message")
        };
    }

    private static string? GetString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static DateTimeOffset? GetDate(JsonElement payload, string property)
    {
        var value = GetString(payload, property);
        // Twilio timestamps are RFC 2822, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
