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
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio implementation of <see cref="ISmsProvider"/>, talking to the REST API over HTTP with Basic
/// auth. All messaging-API calls honour the optional <see cref="TwilioSettings.BaseUrl"/> override; the
/// Lookup call always targets Twilio's Lookup host, which that setting does not govern.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ScheduledStatus = "scheduled";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> options, IAppLogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl.TrimEnd('/');

    private string AccountResource => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}";

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // An unrecognised / out-of-range number can come back as 404; treat that as "not usable".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneLookupResult(false, null);
        }

        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validProp) && validProp.ValueKind == JsonValueKind.True;
        string? canonical = root.TryGetProperty("phone_number", out var numberProp) && numberProp.ValueKind == JsonValueKind.String
            ? numberProp.GetString()
            : null;

        return new PhoneLookupResult(valid, canonical);
    }

    public async Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync($"{AccountResource}/Messages.json", form, cancellationToken);
    }

    public async Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and a fixed send time in ISO-8601 UTC.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync($"{AccountResource}/Messages.json", form, cancellationToken);
    }

    public async Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync($"{AccountResource}/Messages/{messageSid}.json", form, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{AccountResource}/Messages/{messageSid}.json";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        return ParseMessage(doc.RootElement);
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redact the body by updating it to an empty string; the record and its status survive.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        var url = $"{AccountResource}/Messages/{messageSid}.json";
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for this application's own sending number. The DateSent filter is
        // day-granular with these semantics: "DateSent>D" is >= 00:00 of D, and "DateSent<D" is
        // strictly before 00:00 of D. So bound with the lower day and the day AFTER the upper day to
        // include every message on the upper day, then narrow precisely to [from, to] afterwards.
        var fromDay = from.ToUniversalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDayExclusive = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fromEncoded = Uri.EscapeDataString(_settings.FromNumber);

        var url = $"{AccountResource}/Messages.json?From={fromEncoded}"
            + $"&DateSent%3E={fromDay}&DateSent%3C={toDayExclusive}&PageSize=1000";

        var collected = new List<ProviderMessage>();
        string? nextUrl = url;
        var guard = 0;

        while (nextUrl is not null && guard++ < 10_000)
        {
            using var request = CreateRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in messages.EnumerateArray())
                {
                    var message = ParseMessage(element);
                    // Cover the whole range exactly: keep only messages actually sent within [from, to].
                    if (message.DateSent is { } sent && sent >= from && sent <= to)
                    {
                        collected.Add(message);
                    }
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var nextPath = next.GetString();
                if (!string.IsNullOrEmpty(nextPath))
                {
                    // Resolve the provider's next-page path against the messaging base authority.
                    nextUrl = new Uri(new Uri(MessagingBaseUrl), nextPath).ToString();
                }
            }
        }

        return collected;
    }

    private async Task<ProviderMessage> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        return ParseMessage(doc.RootElement);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Parse only the provider's error code / more_info — never its free-text message, which may
        // contain the destination number.
        int? providerCode = null;
        string? moreInfo = null;
        try
        {
            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number)
            {
                providerCode = codeProp.GetInt32();
            }
            if (root.TryGetProperty("more_info", out var infoProp) && infoProp.ValueKind == JsonValueKind.String)
            {
                moreInfo = infoProp.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body: fall through with no provider detail.
        }

        _logger.LogWarning("Twilio API call failed with HTTP {Status} (provider code {Code}).", (int)response.StatusCode, providerCode?.ToString() ?? "n/a");
        throw new TwilioApiException((int)response.StatusCode, providerCode, moreInfo);
    }

    private static ProviderMessage ParseMessage(JsonElement element)
    {
        string sid = GetString(element, "sid") ?? string.Empty;
        string status = GetString(element, "status") ?? string.Empty;
        int? errorCode = GetInt(element, "error_code");
        string? errorMessage = GetString(element, "error_message");
        string? to = GetString(element, "to");
        string? from = GetString(element, "from");
        string? body = GetString(element, "body");
        DateTimeOffset? dateSent = GetDate(element, "date_sent");

        return new ProviderMessage(sid, status, errorCode, errorMessage, to, from, body, dateSent);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String when int.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var raw = GetString(element, name);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
