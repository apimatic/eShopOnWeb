using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio Programmable Messaging implementation of <see cref="IMessageProvider"/>.
/// Contract verified against https://www.twilio.com/docs/messaging/api/message-resource
/// and https://www.twilio.com/docs/messaging/features/message-scheduling.
/// </summary>
public class TwilioMessageProvider : IMessageProvider
{
    private const string DefaultBaseUrl = "https://api.twilio.com";
    private const string ScheduleTypeFixed = "fixed";
    private const string CanceledStatus = "canceled";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _baseUrl;

    public TwilioMessageProvider(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio settings are missing: Twilio:AccountSid and Twilio:AuthToken must be configured (e.g. via user-secrets).");
        }

        _baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        using var response = await PostFormAsync(MessagesUri(), payload, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    public async Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new MessageProviderException("Twilio:MessagingServiceSid must be configured to schedule messages.");
        }

        var payload = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = ScheduleTypeFixed,
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };

        using var response = await PostFormAsync(MessagesUri(), payload, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    public async Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUri(messageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using var document = await ReadJsonAsync(response, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string> { ["Status"] = CanceledStatus };
        using var response = await PostFormAsync(MessageUri(messageSid), payload, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await PostFormAsync(MessageUri(messageSid), payload, cancellationToken);
        await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only.
        // Twilio's DateSent filter is date-granular (yyyy-MM-dd, GMT); the exact
        // date-time range is applied to the returned records below. The upper bound
        // is shifted a day out because DateSent>=X combined with DateSent<=X on the
        // same date yields an empty result.
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={from.UtcDateTime:yyyy-MM-dd}" +
                    $"&DateSent%3C={to.UtcDateTime.AddDays(1):yyyy-MM-dd}" +
                    "&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? nextUri = MessagesUri() + query;

        while (nextUri is not null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            using var document = await ReadJsonAsync(response, cancellationToken);

            if (document.RootElement.TryGetProperty("messages", out var messages))
            {
                foreach (var element in messages.EnumerateArray())
                {
                    results.Add(ParseMessage(element));
                }
            }

            nextUri = null;
            if (document.RootElement.TryGetProperty("next_page_uri", out var nextPage) &&
                nextPage.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(nextPage.GetString()))
            {
                nextUri = _baseUrl + nextPage.GetString();
            }
        }

        return results
            .Where(m =>
            {
                var when = m.DateSent ?? m.DateCreated;
                return when is null || (when >= from && when <= to);
            })
            .ToList();
    }

    private string MessagesUri() => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUri(string messageSid) => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private async Task<HttpResponseMessage> PostFormAsync(string uri, Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(payload);
        var response = await _httpClient.PostAsync(uri, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response;
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? code = null;
        string message = $"Twilio API request failed with status {(int)response.StatusCode}.";
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number)
            {
                code = codeElement.GetInt32();
            }
            if (document.RootElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString() ?? message;
            }
        }
        catch (JsonException)
        {
            // Keep the generic message.
        }

        throw new MessageProviderException(message, code);
    }

    private static ProviderMessage ParseMessage(JsonElement element)
    {
        return new ProviderMessage(
            Sid: element.GetProperty("sid").GetString()!,
            Status: GetString(element, "status"),
            ErrorCode: GetInt(element, "error_code"),
            ErrorMessage: GetString(element, "error_message"),
            To: GetString(element, "to"),
            From: GetString(element, "from"),
            Body: GetString(element, "body"),
            DateSent: GetDate(element, "date_sent"),
            DateCreated: GetDate(element, "date_created"));
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        // Twilio returns RFC 2822 dates, e.g. "Wed, 27 Aug 2026 10:15:00 +0000".
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
