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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of <see cref="ISmsSender"/> over the messaging REST API and the Lookup v2
/// API, using plain HTTP. Every messaging-API call is addressed at <see cref="TwilioSettings.BaseUrl"/>
/// when it is set, otherwise at Twilio's default host; Lookup always uses its own host. Phone numbers
/// and the auth token are never written to logs.
/// </summary>
public class TwilioMessagingService : ISmsSender
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingService> _logger;

    public TwilioMessagingService(HttpClient httpClient, IOptions<TwilioSettings> options, IAppLogger<TwilioMessagingService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    private string MessagingBase =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesCollectionUrl => $"{MessagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageResourceUrl(string sid) => $"{MessagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            return new PhoneNumberValidationResult(false, null, "A phone number is required.");
        }

        // Lookup v2 is served from its own host and is not governed by the messaging BaseUrl override.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawPhoneNumber.Trim())}";
        using var request = BuildRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Twilio returns 404 when the number cannot be parsed into a real destination.
            return new PhoneNumberValidationResult(false, null, "The number is not a valid, reachable destination.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioMessagingException(DescribeFailure("lookup", response.StatusCode, body));
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var pnEl) ? pnEl.GetString() : null;

        return valid && !string.IsNullOrEmpty(canonical)
            ? new PhoneNumberValidationResult(true, canonical, null)
            : new PhoneNumberValidationResult(false, null, "The number is not a valid, reachable destination.");
    }

    public async Task<SentMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toPhoneNumber),
            new("From", _settings.FromNumber),
            new("Body", body)
        };
        return await CreateMessageAsync(form, "send", cancellationToken);
    }

    public async Task<SentMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new TwilioMessagingException("Scheduling requires a configured Twilio:MessagingServiceSid.");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toPhoneNumber),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", FormatIso8601(sendAt))
        };
        return await CreateMessageAsync(form, "schedule", cancellationToken);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var request = BuildRequest(HttpMethod.Post, MessageResourceUrl(providerMessageSid), form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "cancel", cancellationToken);
    }

    public async Task<string> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Get, MessageResourceUrl(providerMessageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioMessagingException(DescribeFailure("status", response.StatusCode, body));
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty;
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Updating the message with an empty Body redacts the text at the provider while the record survives.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var request = BuildRequest(HttpMethod.Post, MessageResourceUrl(providerMessageSid), form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "redact", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for this application's own sending number's messages in the range —
        // the account carries other traffic that must not be counted.
        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append("&PageSize=1000");
        query.Append("&DateSent%3E=").Append(Uri.EscapeDataString(FormatIso8601(from))); // DateSent> (inclusive lower bound)
        query.Append("&DateSent%3C=").Append(Uri.EscapeDataString(FormatIso8601(to)));   // DateSent< (inclusive upper bound)

        var results = new List<ProviderMessage>();
        string? nextUrl = MessagesCollectionUrl + query;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = BuildRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new TwilioMessagingException(DescribeFailure("reconcile", response.StatusCode, body));
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(new ProviderMessage(
                        Sid: GetString(m, "sid") ?? string.Empty,
                        To: GetString(m, "to") ?? string.Empty,
                        From: GetString(m, "from") ?? string.Empty,
                        Status: GetString(m, "status") ?? string.Empty,
                        DateSent: ParseTwilioDate(GetString(m, "date_sent"))));
                }
            }

            var nextPage = root.TryGetProperty("next_page_uri", out var npEl) && npEl.ValueKind == JsonValueKind.String
                ? npEl.GetString()
                : null;
            nextUrl = string.IsNullOrEmpty(nextPage) ? null : MessagingBase + nextPage;
        }

        return results;
    }

    private async Task<SentMessage> CreateMessageAsync(List<KeyValuePair<string, string>> form, string operation, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Post, MessagesCollectionUrl, form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioMessagingException(DescribeFailure(operation, response.StatusCode, body));
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var sid = GetString(root, "sid");
        if (string.IsNullOrEmpty(sid))
        {
            throw new TwilioMessagingException($"Twilio {operation} returned no message sid.");
        }

        return new SentMessage(
            Sid: sid,
            Status: GetString(root, "status") ?? string.Empty,
            DateSent: ParseTwilioDate(GetString(root, "date_sent")));
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, List<KeyValuePair<string, string>>? form = null)
    {
        var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }
        return request;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new TwilioMessagingException(DescribeFailure(operation, response.StatusCode, body));
    }

    /// <summary>
    /// Builds a log-safe failure description: HTTP status plus Twilio's numeric error code only.
    /// Twilio error text can echo the destination number, so it is not included.
    /// </summary>
    private static string DescribeFailure(string operation, HttpStatusCode statusCode, string body)
    {
        int? code = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                code = codeEl.GetInt32();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — ignore; we only surface the HTTP status.
        }

        return code.HasValue
            ? $"Twilio {operation} failed (HTTP {(int)statusCode}, Twilio code {code})."
            : $"Twilio {operation} failed (HTTP {(int)statusCode}).";
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FormatIso8601(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTwilioDate(string? value) =>
        !string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
