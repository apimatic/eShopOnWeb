using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the messaging provider's REST API. The messaging API (send, schedule, fetch,
/// cancel, redact, list) goes through <see cref="TwilioSettings.BaseUrl"/> when configured,
/// verbatim; phone-number lookups are served from the Lookup host, which BaseUrl does not govern.
/// The auth token is used only for the Basic authorization header and is never logged.
/// Destination numbers are never logged; provider error text is sanitized before surfacing.
/// </summary>
public class TwilioSmsProvider : ISmsProvider, IPhoneNumberValidator
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly Regex PhoneLikePattern = new(@"\+?\d[\d\s().-]{5,}\d", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(HttpClient httpClient, TwilioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : settings.BaseUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AccountSid and Twilio:AuthToken must be configured (e.g. via user-secrets).");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagesUrl => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<ValidatedPhoneNumber> ValidateAsync(string rawNumber, string? countryCode = null, CancellationToken cancellationToken = default)
    {
        // The path parameter accepts E.164 or national format; a leading '+' must be percent-encoded.
        var encoded = Uri.EscapeDataString(rawNumber);
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{encoded}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccess(response, cancellationToken);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var result = new ValidatedPhoneNumber
        {
            Valid = root.TryGetProperty("valid", out var valid) && valid.GetBoolean(),
            PhoneNumber = GetString(root, "phone_number"),
            NationalFormat = GetString(root, "national_format")
        };
        if (root.TryGetProperty("validation_errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            result.ValidationErrors = errors.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList();
        }
        return result;
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
        => CreateMessageAsync(to, body, scheduleFor: null, cancellationToken);

    public Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
        => CreateMessageAsync(to, body, sendAt, cancellationToken);

    private async Task<ProviderMessage> CreateMessageAsync(string to, string body, DateTimeOffset? scheduleFor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber must be configured.");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("Body", body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            // The Messaging Service unlocks scheduling; From pins this app's own sending number.
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (scheduleFor.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid must be configured to schedule messages.");
            }
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", scheduleFor.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await _httpClient.PostAsync(MessagesUrl, new FormUrlEncodedContent(form), cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return ParseMessage(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<ProviderMessage?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccess(response, cancellationToken);
        return ParseMessage(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        await EnsureSuccess(response, cancellationToken);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the message text at the provider.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        await EnsureSuccess(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber must be configured.");
        }

        // Ask the provider for this application's own sending number's messages only.
        // The DateSent filters are date-granular and the upper bound is exclusive of the
        // given date, so query one day past and apply the exact date-time range below.
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={from.UtcDateTime:yyyy-MM-dd}",
            $"{Uri.EscapeDataString("DateSent<")}={to.UtcDateTime.Date.AddDays(1):yyyy-MM-dd}",
            "PageSize=1000"
        });

        var results = new List<ProviderMessage>();
        string? nextUrl = $"{MessagesUrl}?{query}";
        while (nextUrl != null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            await EnsureSuccess(response, cancellationToken);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                results.AddRange(messages.EnumerateArray().Select(ParseMessage));
            }

            var nextPageUri = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(nextPageUri) ? null : _messagingBaseUrl + nextPageUri;
        }

        return results
            .Where(m => (m.DateSent ?? m.DateCreated) is { } when && when >= from && when <= to)
            .ToList();
    }

    private static ProviderMessage ParseMessage(JsonElement element) => new()
    {
        Sid = GetString(element, "sid") ?? string.Empty,
        Status = GetString(element, "status") ?? string.Empty,
        To = GetString(element, "to"),
        From = GetString(element, "from"),
        Body = GetString(element, "body"),
        ErrorCode = element.TryGetProperty("error_code", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetInt32() : null,
        ErrorMessage = GetString(element, "error_message"),
        DateCreated = ParseRfc2822(GetString(element, "date_created")),
        DateSent = ParseRfc2822(GetString(element, "date_sent"))
    };

    private static ProviderMessage ParseMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseMessage(doc.RootElement);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ParseRfc2822(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        string? providerCode = null;
        string? providerMessage = null;
        try
        {
            using var doc = JsonDocument.Parse(detail);
            providerCode = GetString(doc.RootElement, "code") ?? (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32().ToString() : null);
            providerMessage = GetString(doc.RootElement, "message");
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with the status code only.
        }

        // Provider error text can embed the destination number; strip anything phone-like
        // so a shopper's number can never reach the logs through an exception message.
        var sanitized = providerMessage == null ? null : PhoneLikePattern.Replace(providerMessage, "[number]");
        throw new SmsProviderException($"Messaging provider rejected the request ({(int)response.StatusCode} {response.StatusCode}, code {providerCode ?? "n/a"}): {sanitized ?? "no detail"}");
    }
}
