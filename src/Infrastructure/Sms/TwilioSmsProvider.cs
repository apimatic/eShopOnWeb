using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Talks to Twilio's REST API over HTTP exactly as its documentation specifies: HTTP Basic auth
/// (Account SID / Auth Token), form-encoded request bodies, PascalCase parameters and snake_case
/// responses. The classic Message resource lives on the messaging host (overridable via
/// <c>Twilio:BaseUrl</c>); phone-number Lookup lives on its own host and is never overridden.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    // Default messaging host for the classic /2010-04-01 Message resource.
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    // Lookup is served from its own host and is deliberately not governed by Twilio:BaseUrl.
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _messagingBaseUrl;
    private readonly string _basicAuth;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
            throw new SmsProviderException("Twilio:AccountSid and Twilio:AuthToken must be configured.");

        _messagingBaseUrl = (string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!)
            .TrimEnd('/');

        _basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
    }

    private string AccountSid => _settings.AccountSid!;

    // -- Lookup -------------------------------------------------------------------------------------

    public async Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";

        using var request = BuildRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        // A number the provider cannot even recognize comes back as 404 — treat as "not usable".
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new PhoneNumberLookupResult { IsValid = false, ValidationError = "The number is not a recognizable phone number." };

        if (!response.IsSuccessStatusCode)
            throw ProviderError("look up a phone number", response.StatusCode, content);

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");

        if (!valid || string.IsNullOrEmpty(canonical))
        {
            var reason = DescribeValidationErrors(root) ?? "The number is not a usable SMS destination.";
            return new PhoneNumberLookupResult { IsValid = false, ValidationError = reason };
        }

        return new PhoneNumberLookupResult { IsValid = true, CanonicalNumber = canonical };
    }

    // -- Send / schedule --------------------------------------------------------------------------

    public async Task<ProviderMessage> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (request.SendAt.HasValue)
        {
            // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt.
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
                throw new SmsProviderException("Twilio:MessagingServiceSid must be configured to schedule a message.");

            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid!));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }
        else
        {
            // Immediate send from this application's own configured sending number.
            if (string.IsNullOrWhiteSpace(_settings.FromNumber))
                throw new SmsProviderException("Twilio:FromNumber must be configured to send a message.");

            form.Add(new("From", _settings.FromNumber!));
        }

        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages.json";
        using var httpRequest = BuildRequest(HttpMethod.Post, url, form);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ProviderError("send a message", response.StatusCode, content);

        using var doc = JsonDocument.Parse(content);
        return ReadMessage(doc.RootElement);
    }

    // -- Read -------------------------------------------------------------------------------------

    public async Task<ProviderMessage> FetchAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages/{Uri.EscapeDataString(providerMessageId)}.json";
        using var request = BuildRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ProviderError("fetch a message", response.StatusCode, content);

        using var doc = JsonDocument.Parse(content);
        return ReadMessage(doc.RootElement);
    }

    // -- Cancel a scheduled message ---------------------------------------------------------------

    public async Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        await PostMessageUpdateAsync(providerMessageId, form, "cancel a scheduled message", cancellationToken);
    }

    // -- Redact the body --------------------------------------------------------------------------

    public async Task RedactBodyAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        // Redaction is an update that sets Body to an empty string.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        await PostMessageUpdateAsync(providerMessageId, form, "redact a message body", cancellationToken);
    }

    private async Task PostMessageUpdateAsync(string providerMessageId, List<KeyValuePair<string, string>> form, string action, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages/{Uri.EscapeDataString(providerMessageId)}.json";
        using var request = BuildRequest(HttpMethod.Post, url, form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw ProviderError(action, response.StatusCode, content);
        }
    }

    // -- Reconciliation list ----------------------------------------------------------------------

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
            throw new SmsProviderException("Twilio:FromNumber must be configured to reconcile messages.");

        // Ask the provider directly for this sender's messages in the range. Date filters are
        // whole-day (YYYY-MM-DD, GMT). The provider treats DateSent> as inclusive of its day but
        // DateSent< as exclusive of its day, so the upper bound is the day AFTER `to` to keep `to`'s
        // day in range; the exact-time refinement below then trims the boundary days.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toExclusiveDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var url =
            $"{_messagingBaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages.json" +
            $"?From={Uri.EscapeDataString(_settings.FromNumber!)}" +
            $"&{Uri.EscapeDataString("DateSent>")}={fromDate}" +
            $"&{Uri.EscapeDataString("DateSent<")}={toExclusiveDate}" +
            "&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? nextUrl = url;

        while (nextUrl is not null)
        {
            using var request = BuildRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw ProviderError("list messages", response.StatusCode, content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in messages.EnumerateArray())
                {
                    var message = ReadMessage(element);
                    // Trim boundary-day messages that fall outside the exact requested window.
                    if (message.DateSent is { } sent && (sent < from || sent > to))
                        continue;
                    results.Add(message);
                }
            }

            // Classic list pages return a relative next_page_uri; resolve it against the messaging host.
            var next = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : _messagingBaseUrl + next;
        }

        return results;
    }

    // -- helpers ----------------------------------------------------------------------------------

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>>? form = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuth);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (form is not null)
            request.Content = new FormUrlEncodedContent(form);
        return request;
    }

    private static ProviderMessage ReadMessage(JsonElement element) => new()
    {
        Sid = GetString(element, "sid") ?? string.Empty,
        Status = GetString(element, "status") ?? string.Empty,
        ErrorCode = GetInt(element, "error_code"),
        ErrorMessage = GetString(element, "error_message"),
        From = GetString(element, "from"),
        To = GetString(element, "to"),
        DateSent = ParseDate(GetString(element, "date_sent"))
    };

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var n) ? n : null,
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null,
            _ => null
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        // Classic API dates are RFC 2822 (e.g. "Thu, 24 Aug 2023 05:01:45 +0000"), which the standard
        // parser handles; fall back to a plain parse for any ISO-8601 value.
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? DescribeValidationErrors(JsonElement root)
    {
        if (!root.TryGetProperty("validation_errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            return null;

        var reasons = new List<string>();
        foreach (var error in errors.EnumerateArray())
        {
            var text = error.GetString();
            if (!string.IsNullOrEmpty(text))
                reasons.Add(text);
        }

        return reasons.Count == 0 ? null : $"The number is not valid ({string.Join(", ", reasons)}).";
    }

    /// <summary>
    /// Builds an exception from a provider error body without ever exposing the auth token. The
    /// provider's own error code and message are included to aid diagnosis.
    /// </summary>
    private static SmsProviderException ProviderError(string action, HttpStatusCode statusCode, string content)
    {
        int? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            code = GetInt(doc.RootElement, "code");
            message = GetString(doc.RootElement, "message");
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to the status code alone.
        }

        var detail = code.HasValue ? $" (code {code})" : string.Empty;
        var description = string.IsNullOrEmpty(message) ? string.Empty : $": {message}";
        return new SmsProviderException($"Failed to {action}. Provider returned {(int)statusCode}{detail}{description}");
    }
}
