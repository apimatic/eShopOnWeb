using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written Twilio messaging (v2010) client. Built to the OpenAPI contract:
/// <c>POST/GET /2010-04-01/Accounts/{AccountSid}/Messages.json</c> and
/// <c>POST/GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json</c>, form-urlencoded
/// bodies, HTTP Basic auth (AccountSid:AuthToken).
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    private string MessagesCollectionUrl =>
        $"{_settings.ResolveMessagingBaseUrl()}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstanceUrl(string messageSid) =>
        $"{_settings.ResolveMessagingBaseUrl()}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    public Task<TwilioMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return PostFormAsync(MessagesCollectionUrl, form, cancellationToken);
    }

    public Task<TwilioMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed per the spec; the
        // provider holds the message until SendAt and sends it itself.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return PostFormAsync(MessagesCollectionUrl, form, cancellationToken);
    }

    public async Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageInstanceUrl(messageSid));
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<TwilioMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return PostFormAsync(MessageInstanceUrl(messageSid), form, cancellationToken);
    }

    public Task<TwilioMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the message text at the provider (per the spec's UpdateMessage).
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        return PostFormAsync(MessageInstanceUrl(messageSid), form, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesByFromAsync(
        string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for messages sent from our own number (the account carries
        // other traffic). The DateSent filter is date-granular: the provider anchors a
        // date-only bound to the START of that day, so the upper bound is pushed to the day
        // after 'to' to keep the whole range's final day in the result. This deliberately
        // returns a superset; the caller narrows to the exact instant range afterwards.
        var fromDate = from.ToUniversalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = new StringBuilder();
        query.Append("From=").Append(Uri.EscapeDataString(fromNumber));
        query.Append("&DateSent%3E=").Append(Uri.EscapeDataString(fromDate)); // DateSent>= (on and after)
        query.Append("&DateSent%3C=").Append(Uri.EscapeDataString(toDate));   // DateSent<= (on and before)
        query.Append("&PageSize=1000");

        var results = new List<TwilioMessage>();
        string? nextUrl = $"{MessagesCollectionUrl}?{query}";
        var pageGuard = 0;

        while (nextUrl is not null && pageGuard++ < 1000)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            var page = await SendForJsonAsync<TwilioMessageListPage>(request, cancellationToken).ConfigureAwait(false);
            if (page?.Messages is { Count: > 0 })
                results.AddRange(page.Messages);

            // next_page_uri is a path relative to the provider host; resolve against the base.
            nextUrl = string.IsNullOrWhiteSpace(page?.NextPageUri)
                ? null
                : CombineWithBaseHost(page!.NextPageUri!);
        }

        return results;
    }

    private string CombineWithBaseHost(string relativeOrAbsolute)
    {
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var abs))
            return abs.ToString();

        var baseUri = new Uri(_settings.ResolveMessagingBaseUrl(), UriKind.Absolute);
        return new Uri(baseUri, relativeOrAbsolute).ToString();
    }

    private async Task<TwilioMessage> PostFormAsync(string url, IDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TwilioMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var message = await SendForJsonAsync<TwilioMessage>(request, cancellationToken).ConfigureAwait(false);
        return message ?? throw new TwilioApiException(System.Net.HttpStatusCode.OK, null, "Empty response body from Twilio.");
    }

    private async Task<T?> SendForJsonAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ApplyAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var (code, msg) = TryParseError(payload);
            // Log the operation and provider error only — never the destination or body.
            _logger.LogWarning("Twilio {Method} {Path} returned {Status} (code {Code}).",
                request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode, code);
            throw new TwilioApiException(response.StatusCode, code, msg);
        }

        if (string.IsNullOrWhiteSpace(payload))
            return default;

        return JsonSerializer.Deserialize<T>(payload);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var raw = Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    private static (int? code, string? message) TryParseError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            int? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetInt32()
                : null;
            string? message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
