using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio implementation of the messaging provider and phone number validator, using
/// plain HTTP against the confirmed Twilio REST contracts:
/// - Messaging API (send/schedule/cancel/fetch/redact/list): POST/GET under
///   {BaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages[...].json. BaseUrl defaults to
///   https://api.twilio.com and is used verbatim for every messaging-API call when set.
/// - Lookup API (validation/canonical form): GET https://lookups.twilio.com/v2/PhoneNumbers/{number}.
///   The messaging BaseUrl override does not govern this host.
/// Auth is HTTP Basic with AccountSid:AuthToken. Neither the auth token nor destination
/// numbers are ever logged.
/// </summary>
public class TwilioMessagingProvider : IMessagingProvider, IPhoneNumberValidator
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly Regex PhoneNumberPattern = new(@"\+?\d[\d\s().-]{5,}\d", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingProvider> _logger;

    public TwilioMessagingProvider(HttpClient httpClient, IOptions<TwilioSettings> settings, IAppLogger<TwilioMessagingProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var accountSid = RequireSetting(_settings.AccountSid, nameof(_settings.AccountSid));
        var authToken = RequireSetting(_settings.AuthToken, nameof(_settings.AuthToken));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}")));
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesUrl =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string messageSid) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    public async Task<ProviderMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fromNumber = RequireSetting(_settings.FromNumber, nameof(_settings.FromNumber));
        var response = await PostMessageAsync(new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = fromNumber,
            ["Body"] = body
        }, cancellationToken);

        return new ProviderMessageResult(GetString(response, "sid")!, GetString(response, "status") ?? "queued");
    }

    public async Task<ProviderMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var messagingServiceSid = RequireSetting(_settings.MessagingServiceSid, nameof(_settings.MessagingServiceSid));
        var response = await PostMessageAsync(new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = messagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        }, cancellationToken);

        return new ProviderMessageResult(GetString(response, "sid")!, GetString(response, "status") ?? "scheduled");
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        await PostToMessageResourceAsync(providerMessageSid, new Dictionary<string, string>
        {
            ["Status"] = "canceled"
        }, cancellationToken);
    }

    public async Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        var json = await ReadAndEnsureSuccessAsync(response, cancellationToken);
        return ParseProviderMessage(json);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Posting an empty Body disposes of the message text at the provider while the
        // record of the message (and its outcome) survives.
        await PostToMessageResourceAsync(providerMessageSid, new Dictionary<string, string>
        {
            ["Body"] = string.Empty
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = RequireSetting(_settings.FromNumber, nameof(_settings.FromNumber));

        // Ask the provider for only this application's own sending number's messages,
        // filtered by date range at the source.
        var url = MessagesUrl +
            $"?From={Uri.EscapeDataString(fromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
            $"&DateSent%3C={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
            "&PageSize=100";

        var messages = new List<ProviderMessage>();
        while (url is not null)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var json = await ReadAndEnsureSuccessAsync(response, cancellationToken);

            if (json.TryGetProperty("messages", out var page))
            {
                foreach (var message in page.EnumerateArray())
                {
                    messages.Add(ParseProviderMessage(message));
                }
            }

            url = null;
            if (json.TryGetProperty("next_page_uri", out var nextPageUri) && nextPageUri.ValueKind == JsonValueKind.String)
            {
                var relative = nextPageUri.GetString();
                if (!string.IsNullOrEmpty(relative))
                {
                    url = $"{MessagingBaseUrl}{relative}";
                }
            }
        }

        return messages;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Twilio Lookup v2: basic formatting/validation lookup. This capability is served
        // from the lookups host, which the messaging BaseUrl override does not govern.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioException($"Twilio Lookup request failed with status {(int)response.StatusCode}.");
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        var isValid = root.TryGetProperty("valid", out var valid) && valid.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var validationErrors) && validationErrors.ValueKind == JsonValueKind.Array)
        {
            errors.AddRange(validationErrors.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!));
        }

        return new PhoneNumberValidationResult(isValid, isValid ? canonical : null, errors);
    }

    private async Task<JsonElement> PostMessageAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(MessagesUrl, content, cancellationToken);
        return await ReadAndEnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<JsonElement> PostToMessageResourceAsync(string messageSid, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), content, cancellationToken);
        return await ReadAndEnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task<JsonElement> ReadAndEnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new TwilioException($"Twilio request failed with status {(int)response.StatusCode} and a non-JSON response.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var code = GetString(json.RootElement, "code") ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            var message = Sanitize(GetString(json.RootElement, "message") ?? "unknown error");
            json.Dispose();
            throw new TwilioException($"Twilio error {code}: {message}");
        }

        var result = json.RootElement.Clone();
        json.Dispose();
        return result;
    }

    /// <summary>Removes anything looking like a phone number from provider error text.</summary>
    private static string Sanitize(string text) => PhoneNumberPattern.Replace(text, "***");

    private static ProviderMessage ParseProviderMessage(JsonElement json)
    {
        DateTimeOffset? dateSent = null;
        var dateSentRaw = GetString(json, "date_sent");
        if (!string.IsNullOrEmpty(dateSentRaw) &&
            DateTimeOffset.TryParse(dateSentRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }

        return new ProviderMessage(GetString(json, "sid")!, GetString(json, "status") ?? "unknown", dateSent);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private string RequireSetting(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new TwilioException($"Twilio setting '{name}' is not configured.");
        }
        return value;
    }
}
