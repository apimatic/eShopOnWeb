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
/// Talks to Twilio over plain HTTP. Messaging calls (send, schedule, fetch, cancel, redact, list)
/// go to the configured messaging base address (Twilio:BaseUrl when set, otherwise Twilio's default
/// messaging host). Number validation goes to Twilio's separate Lookup host, which the BaseUrl
/// override does not govern.
/// </summary>
public class TwilioMessagingService : ITwilioMessagingService
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingService(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl!.TrimEnd('/');

        // Basic auth with Account SID + Auth Token. The token is only ever placed in this header,
        // which is never logged.
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public string ConfiguredFromNumber => _options.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // The provider returns 404 for a number it cannot parse at all — treat that as "not usable".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null);
        }

        await EnsureSuccessAsync(response, "lookup", cancellationToken);

        using var doc = await ParseJsonAsync(response, cancellationToken);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) &&
                    validEl.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");

        return new PhoneNumberLookupResult(valid, valid ? canonical : null);
    }

    public async Task<ProviderMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };

        return await PostMessageAsync(MessagesUrl(), form, "send", cancellationToken);
    }

    public async Task<ProviderMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires the Messaging Service. The sending number is pinned so scheduled
        // messages reconcile against the same configured From number as immediate ones.
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["From"] = _options.FromNumber,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = FormatIso8601(sendAt),
            ["Body"] = body
        };

        return await PostMessageAsync(MessagesUrl(), form, "schedule", cancellationToken);
    }

    public async Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = MessageUrl(messageSid);
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "fetch", cancellationToken);

        using var doc = await ParseJsonAsync(response, cancellationToken);
        return ReadMessage(doc.RootElement);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // A just-scheduled message can take a few seconds to become updatable at the account endpoint.
        // Since calling off a follow-up is a correctness requirement, retry a transient 404 rather than
        // giving up (there is ample margin: the send is days away).
        const int maxAttempts = 6;
        for (var attempt = 1; ; attempt++)
        {
            var form = new Dictionary<string, string> { ["Status"] = "canceled" };
            using var content = new FormUrlEncodedContent(form);
            using var response = await _httpClient.PostAsync(MessageUrl(messageSid), content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (response.StatusCode == HttpStatusCode.NotFound && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            await EnsureSuccessAsync(response, "cancel", cancellationToken);
        }
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Updating the body to an empty string redacts the text at the provider while keeping the
        // record of the message and what became of it.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), content, cancellationToken);
        await EnsureSuccessAsync(response, "redact", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();

        // Ask the provider directly for this application's own sending number, filtered by the range,
        // rather than fetching a wider answer and filtering it here.
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_options.FromNumber)}",
            $"DateSent{Uri.EscapeDataString(">")}={Uri.EscapeDataString(FormatIso8601(from))}",
            $"DateSent{Uri.EscapeDataString("<")}={Uri.EscapeDataString(FormatIso8601(to))}",
            "PageSize=1000"
        });

        string? nextUrl = $"{MessagesUrl()}?{query}";
        var messagingBaseUri = new Uri(_messagingBaseUrl);

        while (nextUrl is not null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            await EnsureSuccessAsync(response, "list", cancellationToken);

            using var doc = await ParseJsonAsync(response, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    results.Add(ReadMessage(message));
                }
            }

            // Follow the provider's own pagination cursor until the range is fully covered.
            var nextPageUri = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(nextPageUri) ? null : new Uri(messagingBaseUri, nextPageUri).ToString();
        }

        return results;
    }

    // ----- helpers -----

    private string MessagesUrl() =>
        $"{_messagingBaseUrl}/{ApiVersion}/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageUrl(string messageSid) =>
        $"{_messagingBaseUrl}/{ApiVersion}/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private async Task<ProviderMessage> PostMessageAsync(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, operation, cancellationToken);

        using var doc = await ParseJsonAsync(response, cancellationToken);
        return ReadMessage(doc.RootElement);
    }

    private static string FormatIso8601(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static ProviderMessage ReadMessage(JsonElement element)
    {
        var sid = GetString(element, "sid") ?? string.Empty;
        var status = GetString(element, "status") ?? string.Empty;
        var errorCode = GetErrorCode(element);
        var to = GetString(element, "to");
        var from = GetString(element, "from");
        var body = GetString(element, "body");
        var dateSent = ParseDate(GetString(element, "date_sent"));

        return new ProviderMessage(sid, status, errorCode, to, from, dateSent, body);
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
    }

    private static string? GetErrorCode(JsonElement element)
    {
        if (!element.TryGetProperty("error_code", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => value.GetString(),
            _ => null
        };
    }

    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        // Twilio returns RFC 2822 timestamps, e.g. "Mon, 30 Aug 2021 20:36:27 +0000".
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Read only the provider's numeric error code from the body — never the free-text message,
        // which can contain a shopper's phone number.
        string? providerErrorCode = null;
        try
        {
            using var doc = await ParseJsonAsync(response, cancellationToken);
            providerErrorCode = GetErrorCode(doc.RootElement);
        }
        catch
        {
            // Non-JSON error body: fall back to just the HTTP status.
        }

        throw new TwilioApiException(response.StatusCode, providerErrorCode, operation);
    }
}
