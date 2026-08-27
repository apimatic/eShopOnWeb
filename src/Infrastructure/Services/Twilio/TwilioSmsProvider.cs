using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written Twilio client built against the OpenAPI specifications in /api-specs:
///   - twilio_api_v2010: Messages (create/fetch/list/update) on https://api.twilio.com
///   - twilio_lookups_v2: phone number validation on https://lookups.twilio.com
/// Auth: HTTP Basic with AccountSid:AuthToken (security scheme accountSid_authToken).
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private const string DateSentFilterFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(HttpClient httpClient, TwilioSettings settings, ILogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : settings.BaseUrl!.TrimEnd('/');

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookups v2: GET /v2/PhoneNumbers/{PhoneNumber} (served from lookups.twilio.com;
        // the messaging BaseUrl override does not apply here).
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult { IsValid = false, Error = "The provider does not recognize this as a valid phone number." };
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await ReadJsonAsync(response, cancellationToken);
        var isValid = payload.TryGetProperty("valid", out var validProp) && validProp.ValueKind == JsonValueKind.True;
        var canonical = payload.TryGetProperty("phone_number", out var numberProp) && numberProp.ValueKind == JsonValueKind.String
            ? numberProp.GetString()
            : null;

        return new PhoneNumberValidationResult
        {
            IsValid = isValid && canonical is not null,
            CanonicalNumber = canonical,
            Error = isValid ? null : "The provider does not consider this a usable destination."
        };
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        return CreateMessageAsync(to, body, sendAt: null, cancellationToken);
    }

    public Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        return CreateMessageAsync(to, body, sendAt, cancellationToken);
    }

    public async Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return MapMessage(await ReadJsonAsync(response, cancellationToken));
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage: POST Messages/{Sid}.json with Status=canceled
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await _httpClient.PostAsync(MessageUrl(providerMessageSid), form, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return MapMessage(await ReadJsonAsync(response, cancellationToken));
    }

    public async Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage: POST Messages/{Sid}.json with Body="" redacts the message text.
        // A message still in flight (e.g. "accepted") cannot be updated yet and Twilio
        // answers 404/20404 until it settles, so retry briefly before giving up.
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            using var response = await _httpClient.PostAsync(
                MessageUrl(providerMessageSid),
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty }),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // ListMessage: GET Messages.json filtered by this application's own sending
        // number (From) and the requested sent-date range, so traffic belonging to
        // other applications on the same account is never pulled.
        var query = new Dictionary<string, string>
        {
            ["From"] = _settings.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString(DateSentFilterFormat, CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString(DateSentFilterFormat, CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json?{BuildQuery(query)}";

        var messages = new List<ProviderMessage>();
        while (url is not null)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var payload = await ReadJsonAsync(response, cancellationToken);

            if (payload.TryGetProperty("messages", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                messages.AddRange(items.EnumerateArray().Select(MapMessage));
            }

            url = payload.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? _messagingBaseUrl + next.GetString()
                : null;
        }

        return messages;
    }

    private async Task<ProviderMessage> CreateMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        // CreateMessage: POST Messages.json (application/x-www-form-urlencoded).
        // From and MessagingServiceSid are both supplied: the spec allows a specific
        // sender from the service's pool, scheduling requires the messaging service,
        // and reconciliation keys off the configured FromNumber.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body
        };

        if (sendAt.HasValue)
        {
            form["ScheduleType"] = "fixed";
            form["SendAt"] = sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        using var response = await _httpClient.PostAsync(
            $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json",
            new FormUrlEncodedContent(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return MapMessage(await ReadJsonAsync(response, cancellationToken));
    }

    private string MessageUrl(string messageSid) =>
        $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private static string BuildQuery(Dictionary<string, string> parameters) =>
        string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? message = null;
        int? code = null;
        try
        {
            var payload = await ReadJsonAsync(response, cancellationToken);
            if (payload.TryGetProperty("message", out var messageProp))
            {
                message = messageProp.GetString();
            }
            if (payload.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number)
            {
                code = codeProp.GetInt32();
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic message below.
        }

        // Never include request details here: the destination number is PII and the
        // auth token is a secret.
        _logger.LogWarning("Twilio request failed with status {StatusCode}, provider error {ErrorCode}", (int)response.StatusCode, code);
        throw new SmsProviderException(message ?? $"Twilio request failed with status {(int)response.StatusCode}.", code);
    }

    private static ProviderMessage MapMessage(JsonElement element)
    {
        return new ProviderMessage
        {
            Sid = GetString(element, "sid") ?? string.Empty,
            Status = GetString(element, "status"),
            To = GetString(element, "to"),
            From = GetString(element, "from"),
            Body = GetString(element, "body"),
            ErrorCode = element.TryGetProperty("error_code", out var errorCode) && errorCode.ValueKind == JsonValueKind.Number
                ? errorCode.GetInt32()
                : null,
            ErrorMessage = GetString(element, "error_message"),
            DateCreated = GetDate(element, "date_created"),
            DateSent = GetDate(element, "date_sent")
        };
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        var text = GetString(element, property);
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
