using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for Twilio's messaging API, built to the OpenAPI contract in
/// <c>api-specs/twilio/twilio_api_v2010</c> (the 2010-04-01 Message resource). HTTP Basic auth
/// (AccountSid:AuthToken) is applied by the configured <see cref="HttpClient"/>. Every messaging
/// call is issued against the messaging base address — the <c>Twilio:BaseUrl</c> override when set,
/// otherwise the provider default <c>https://api.twilio.com</c>.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioOptions> options, IAppLogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string SendingNumber => _options.FromNumber ?? string.Empty;

    private string MessagingBase =>
        string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultBaseUrl : _options.BaseUrl!.TrimEnd('/');

    private string MessagesResource => $"{MessagingBase}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageResource(string sid) => $"{MessagingBase}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _options.FromNumber ?? string.Empty,
            ["Body"] = body
        };
        return await CreateMessageAsync(form, scheduled: false, cancellationToken);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        // Per the spec, scheduling a message requires a Messaging Service and ScheduleType=fixed with SendAt.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _options.MessagingServiceSid ?? string.Empty,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["Body"] = body
        };
        return await CreateMessageAsync(form, scheduled: true, cancellationToken);
    }

    private async Task<SmsSendResult> CreateMessageAsync(Dictionary<string, string> form, bool scheduled, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessagesResource, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(payload);
            _logger.LogWarning("Provider rejected a {Kind} message ({StatusCode}); code {Code}.",
                scheduled ? "scheduled" : "immediate", (int)response.StatusCode, code);
            return new SmsSendResult(Accepted: false, Sid: null, Status: null, ErrorCode: code, ErrorMessage: message);
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var sid = GetString(root, "sid");
        var status = GetString(root, "status");
        var errorCode = GetInt(root, "error_code");
        var errorMessage = GetString(root, "error_message");
        _logger.LogInformation("Provider accepted message {Sid} with status {Status}.", sid, status);
        return new SmsSendResult(Accepted: true, Sid: sid, Status: status, ErrorCode: errorCode, ErrorMessage: errorMessage);
    }

    public async Task<ProviderMessage?> FetchAsync(string sid, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(MessageResource(sid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        return ParseMessage(document.RootElement);
    }

    public async Task<SmsSendResult> CancelScheduledAsync(string sid, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageResource(sid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(payload);
            _logger.LogWarning("Provider did not cancel message {Sid} ({StatusCode}); code {Code}.", sid, (int)response.StatusCode, code);
            return new SmsSendResult(Accepted: false, Sid: sid, Status: null, ErrorCode: code, ErrorMessage: message);
        }

        using var document = JsonDocument.Parse(payload);
        var status = GetString(document.RootElement, "status");
        _logger.LogInformation("Cancelled scheduled message {Sid}; status now {Status}.", sid, status);
        return new SmsSendResult(Accepted: true, Sid: sid, Status: status, ErrorCode: null, ErrorMessage: null);
    }

    public async Task<bool> RedactBodyAsync(string sid, CancellationToken cancellationToken)
    {
        // Redact the body at the provider: the spec's UpdateMessage redacts text when Body is empty.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageResource(sid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, _) = ParseError(payload);
            _logger.LogWarning("Provider did not redact message {Sid} ({StatusCode}); code {Code}.", sid, (int)response.StatusCode, code);
            return false;
        }
        _logger.LogInformation("Redacted body of message {Sid} at the provider.", sid);
        return true;
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(string fromE164, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var fromStamp = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toStamp = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Server-side filter: this sending number's traffic within the range. The date filters are the
        // literally-named `DateSent>` / `DateSent<` query parameters (encoded here).
        var query = $"?From={Uri.EscapeDataString(fromE164)}" +
                    $"&DateSent%3E={Uri.EscapeDataString(fromStamp)}" +
                    $"&DateSent%3C={Uri.EscapeDataString(toStamp)}" +
                    "&PageSize=1000";
        var nextUrl = MessagesResource + query;

        var results = new List<ProviderMessage>();
        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    results.Add(ParseMessage(message));
                }
            }

            // Follow next_page_uri (a relative URI) against the same messaging base until it is null.
            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var relative = next.GetString();
                if (!string.IsNullOrEmpty(relative))
                {
                    nextUrl = MessagingBase + relative;
                }
            }
        }

        return results;
    }

    private static ProviderMessage ParseMessage(JsonElement root)
    {
        return new ProviderMessage(
            Sid: GetString(root, "sid") ?? string.Empty,
            Status: GetString(root, "status"),
            ErrorCode: GetInt(root, "error_code"),
            ErrorMessage: GetString(root, "error_message"),
            From: GetString(root, "from"),
            To: GetString(root, "to"),
            Body: GetString(root, "body"),
            DateSent: ParseDate(GetString(root, "date_sent")));
    }

    private static (int? Code, string? Message) ParseError(string payload)
    {
        // Twilio's error model: { "code": 21211, "message": "...", "more_info": "...", "status": 400 }
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            return (GetInt(root, "code"), GetString(root, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        // Message date fields are RFC-2822, e.g. "Fri, 24 May 2019 17:18:28 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
