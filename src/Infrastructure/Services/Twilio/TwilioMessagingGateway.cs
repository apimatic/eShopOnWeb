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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio implementation of <see cref="ISmsGateway"/> over the Twilio REST API using plain
/// HTTP, so the messaging base address can be overridden per <see cref="TwilioSettings.BaseUrl"/>
/// while phone-number Lookup continues to use its own host.
///
/// This class never logs destination numbers or message bodies.
/// </summary>
public class TwilioMessagingGateway : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";
    private const int PageSize = 1000;

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesResourceUrl =>
        $"{MessagingBaseUrl}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageResourceUrl(string messageSid) =>
        $"{MessagingBaseUrl}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    public async Task<PhoneNumberLookupResult> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup lives on its own host and is deliberately not affected by Twilio:BaseUrl.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // Twilio returns 404 for a number it cannot parse/validate; treat that as "not valid".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null);
        }

        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        string? canonical = null;
        if (root.TryGetProperty("phone_number", out var numberEl) && numberEl.ValueKind == JsonValueKind.String)
        {
            canonical = numberEl.GetString();
        }

        return new PhoneNumberLookupResult(valid, valid ? canonical : null);
    }

    public async Task<GatewayMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        // Immediate sends go from the configured number, so the provider's record of them
        // carries that number in its "from" field — which is what reconciliation filters on.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(MessagesResourceUrl, form, cancellationToken);
    }

    public async Task<GatewayMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service; From is not used with a scheduled send.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(MessagesResourceUrl, form, cancellationToken);
    }

    public async Task<GatewayMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageResourceUrl(providerMessageSid), form, cancellationToken);
    }

    public async Task<GatewayMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageResourceUrl(providerMessageSid), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        return MapMessage(doc.RootElement);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Posting an empty Body redacts the text at the provider while the message record and
        // its delivery outcome survive.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageResourceUrl(providerMessageSid), form, cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for messages from the configured sending number (server-side
        // filter), bounded by GMT date so the whole range is covered; we then narrow to the
        // exact [from, to] window client-side. Other numbers on the account are never fetched.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}"
            + $"&DateSent%3E%3D={fromDate}"   // DateSent>=
            + $"&DateSent%3C%3D={toDate}"      // DateSent<=
            + $"&PageSize={PageSize}";

        var nextUrl = MessagesResourceUrl + query;
        var results = new List<GatewayMessage>();

        while (nextUrl is not null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in messages.EnumerateArray())
                {
                    var message = MapMessage(element);
                    if (message.DateSent is null || (message.DateSent >= from && message.DateSent <= to))
                    {
                        results.Add(message);
                    }
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var nextPath = next.GetString();
                if (!string.IsNullOrEmpty(nextPath))
                {
                    // next_page_uri is a path relative to the messaging host.
                    nextUrl = MessagingBaseUrl + nextPath;
                }
            }
        }

        return results;
    }

    private async Task<GatewayMessage> PostMessageAsync(string url, IDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        return MapMessage(doc.RootElement);
    }

    private static GatewayMessage MapMessage(JsonElement element)
    {
        var sid = GetString(element, "sid") ?? string.Empty;
        var status = GetString(element, "status") ?? string.Empty;
        int? errorCode = null;
        if (element.TryGetProperty("error_code", out var errorEl) && errorEl.ValueKind == JsonValueKind.Number)
        {
            errorCode = errorEl.GetInt32();
        }

        var to = GetString(element, "to");
        var fromNumber = GetString(element, "from");
        var body = GetString(element, "body");

        DateTimeOffset? dateSent = null;
        var dateSentRaw = GetString(element, "date_sent");
        if (!string.IsNullOrEmpty(dateSentRaw) &&
            DateTimeOffset.TryParse(dateSentRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }

        return new GatewayMessage(sid, status, errorCode, to, fromNumber, dateSent, body);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Parse Twilio's numeric error code if present, but never surface its free-text message
        // (which can contain the destination number).
        int? providerCode = null;
        try
        {
            using var doc = await ReadJsonAsync(response, cancellationToken);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                providerCode = codeEl.GetInt32();
            }
        }
        catch
        {
            // Non-JSON error body — status alone will have to do.
        }

        throw new SmsGatewayException((int)response.StatusCode, providerCode);
    }
}
