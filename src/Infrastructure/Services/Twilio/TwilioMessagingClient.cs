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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Sends, reads and reconciles messages through the provider's messaging API using plain HTTP.
/// Every call is addressed at <see cref="TwilioSettings.MessagingBaseUrl"/>, so the
/// <c>Twilio:BaseUrl</c> override is honoured verbatim for the whole surface. Basic auth is set
/// per request; the auth token is never placed on a shared/default header and never logged.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string ApiVersion = "2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public Task<TwilioMessageResource> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<TwilioMessageResource> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public async Task<TwilioMessageResource> FetchAsync(string sid, CancellationToken cancellationToken = default)
    {
        var url = $"{MessagesBase()}/{Uri.EscapeDataString(sid)}.json";
        using var request = BuildRequest(HttpMethod.Get, url);
        using var doc = await SendAsync(request, cancellationToken);
        return ParseMessage(doc.RootElement);
    }

    public Task<TwilioMessageResource> CancelScheduledAsync(string sid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(sid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<TwilioMessageResource> RedactBodyAsync(string sid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(sid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<TwilioMessageResource>> ListBySenderAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // Ask the provider for this sender's messages directly. The date filter is applied at the
        // provider at day granularity, so widen it by a day on each side and refine to the exact
        // window below — this guarantees the whole range is covered without a boundary miss.
        var fromDate = from.ToUniversalTime().Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(fromNumber));
        query.Append("&PageSize=1000");
        query.Append("&DateSent%3E=").Append(fromDate); // DateSent>
        query.Append("&DateSent%3C=").Append(toDate);   // DateSent<

        var results = new List<TwilioMessageResource>();
        var nextUrl = $"{MessagesBase()}.json{query}";

        while (nextUrl != null)
        {
            using var request = BuildRequest(HttpMethod.Get, nextUrl);
            using var doc = await SendAsync(request, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in messages.EnumerateArray())
                {
                    var message = ParseMessage(element);
                    // Refine to the exact requested window. Messages the provider has but that were
                    // sent outside [from, to] (only pulled in by the day-level widening) are dropped.
                    if (message.DateSent.HasValue && message.DateSent.Value >= from && message.DateSent.Value <= to)
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
                    nextUrl = $"{BaseHost()}{nextPath}";
                }
            }
        }

        return results;
    }

    private async Task<TwilioMessageResource> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{MessagesBase()}.json";
        using var request = BuildRequest(HttpMethod.Post, url, form);
        using var doc = await SendAsync(request, cancellationToken);
        return ParseMessage(doc.RootElement);
    }

    private async Task<TwilioMessageResource> UpdateMessageAsync(string sid, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{MessagesBase()}/{Uri.EscapeDataString(sid)}.json";
        using var request = BuildRequest(HttpMethod.Post, url, form);
        using var doc = await SendAsync(request, cancellationToken);
        return ParseMessage(doc.RootElement);
    }

    private string BaseHost() => _settings.MessagingBaseUrl.TrimEnd('/');

    private string MessagesBase() =>
        $"{BaseHost()}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages";

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, Dictionary<string, string>? form = null)
    {
        var request = new HttpRequestMessage(method, url);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        if (form != null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, payload);
        }

        return JsonDocument.Parse(payload);
    }

    private static TwilioApiException ParseError(int statusCode, string payload)
    {
        int? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number)
            {
                code = c.GetInt32();
            }
            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            {
                message = m.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic HTTP-status message.
        }

        return new TwilioApiException(statusCode, code, message);
    }

    private static TwilioMessageResource ParseMessage(JsonElement element)
    {
        var sid = GetString(element, "sid") ?? string.Empty;
        var status = GetString(element, "status") ?? string.Empty;
        var errorCode = GetNullableInt(element, "error_code");
        var errorMessage = GetString(element, "error_message");
        var to = GetString(element, "to");
        var from = GetString(element, "from");
        var dateSent = GetDate(element, "date_sent");
        var dateCreated = GetDate(element, "date_created");

        return new TwilioMessageResource(sid, status, errorCode, errorMessage, to, from, dateSent, dateCreated);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetNullableInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var raw = GetString(element, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Twilio returns RFC-2822 timestamps in GMT, e.g. "Fri, 24 May 2019 17:44:46 +0000".
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
