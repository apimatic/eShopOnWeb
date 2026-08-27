using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio messaging API client (the 2010-04-01 Messages resource). All calls go to the
/// configured BaseUrl when set, otherwise to Twilio's default messaging host.
/// Never logs phone numbers, message bodies or credentials.
/// </summary>
public class TwilioMessageProvider : IMessageProvider
{
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessageProvider> _logger;
    private readonly string _messagesUri;

    public string FromNumber => _settings.FromNumber;

    public TwilioMessageProvider(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessageProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl!;
        _messagesUri = $"{baseUrl.TrimEnd('/')}/2010-04-01/Accounts/{_settings.AccountSid}/Messages";

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MessageSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(form, cancellationToken);
    }

    public async Task<MessageSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Services capability; From pins our own sending number
        // from the service's sender pool so reconciliation can find the message later.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(form, cancellationToken);
    }

    public async Task<ProviderMessage?> GetAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"{_messagesUri}/{messageSid}.json", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response, cancellationToken);
        return ParseMessage(json.RootElement);
    }

    public async Task<bool> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await PostFormAsync($"{_messagesUri}/{messageSid}.json", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cancelling message {MessageSid} returned {StatusCode}", messageSid, (int)response.StatusCode);
            return false;
        }
        var json = await ReadJsonAsync(response, cancellationToken);
        return json.RootElement.TryGetProperty("status", out var status) && status.GetString() == "canceled";
    }

    public async Task<bool> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await PostFormAsync($"{_messagesUri}/{messageSid}.json", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Redacting body of message {MessageSid} returned {StatusCode}", messageSid, (int)response.StatusCode);
            return false;
        }
        return true;
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only our own sending number's messages; the account carries
        // other traffic. DateSent filters are GMT dates, so widen to whole days and trim
        // to the exact window below.
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"DateSent%3E%3D={from.UtcDateTime:yyyy-MM-dd}",
            $"DateSent%3C%3D={to.UtcDateTime:yyyy-MM-dd}",
            "PageSize=1000"
        });

        var results = new List<ProviderMessage>();
        string? nextUri = $"{_messagesUri}.json?{query}";
        while (nextUri != null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await ReadJsonAsync(response, cancellationToken);

            if (json.RootElement.TryGetProperty("messages", out var messages))
            {
                foreach (var element in messages.EnumerateArray())
                {
                    var message = ParseMessage(element);
                    var occurred = message.DateSent ?? message.DateCreated;
                    if (occurred.HasValue && occurred.Value >= from && occurred.Value <= to)
                    {
                        results.Add(message);
                    }
                }
            }

            nextUri = null;
            if (json.RootElement.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var path = next.GetString();
                if (!string.IsNullOrEmpty(path))
                {
                    var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl!;
                    nextUri = $"{baseUrl.TrimEnd('/')}{path}";
                }
            }
        }

        return results;
    }

    private async Task<MessageSendResult> PostMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await PostFormAsync($"{_messagesUri}.json", form, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var code = json.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : (int?)null;
            var message = json.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            _logger.LogWarning("Messaging API rejected a send: HTTP {StatusCode}, provider code {Code}", (int)response.StatusCode, code);
            return new MessageSendResult(false, null, "failed", code, message);
        }

        var sid = json.RootElement.TryGetProperty("sid", out var s) ? s.GetString() : null;
        var status = json.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "queued" : "queued";
        return new MessageSendResult(true, sid, status, null, null);
    }

    private Task<HttpResponseMessage> PostFormAsync(string uri, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        return _httpClient.PostAsync(uri, new FormUrlEncodedContent(form), cancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static ProviderMessage ParseMessage(JsonElement element)
    {
        return new ProviderMessage
        {
            Sid = GetString(element, "sid") ?? string.Empty,
            Status = GetString(element, "status") ?? string.Empty,
            ErrorCode = element.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : (int?)null,
            To = GetString(element, "to"),
            From = GetString(element, "from"),
            DateSent = ParseRfc2822(GetString(element, "date_sent")),
            DateCreated = ParseRfc2822(GetString(element, "date_created"))
        };
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
    }
}
