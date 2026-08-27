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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for the Twilio messaging API, built against
/// api-specs/twilio/twilio_api_v2010/twilio_api_v2010.yaml (the authoritative
/// contract). Covers the Message resource: create (immediate and scheduled),
/// fetch, list, update (redact body / cancel scheduled) and delete.
/// </summary>
public class TwilioMessagingClient
{
    internal const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        // Twilio:BaseUrl, when set, is used verbatim as the base address for
        // every messaging-API call; otherwise the spec's default server.
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');
        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
    }

    private string MessagesPath => $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessagePath(string sid) => $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    /// <summary>CreateMessage: send immediately (From) or schedule via the
    /// Messaging Service (ScheduleType=fixed + SendAt).</summary>
    public async Task<TwilioMessageResource> CreateMessageAsync(string to, string body,
        DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("From", _settings.FromNumber),
        };
        if (sendAt.HasValue)
        {
            // Scheduling is a Messaging-Services-only capability per the spec.
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await _httpClient.PostAsync(MessagesPath,
            new FormUrlEncodedContent(form), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken, expectCreated: true);
    }

    /// <summary>FetchMessage.</summary>
    public async Task<TwilioMessageResource> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessagePath(messageSid), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    /// <summary>UpdateMessage with Status=canceled: call off a not-yet-sent message.</summary>
    public async Task<TwilioMessageResource> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await _httpClient.PostAsync(MessagePath(messageSid),
            new FormUrlEncodedContent(form), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    /// <summary>UpdateMessage with an empty Body: redact the message text at the provider.</summary>
    public async Task<TwilioMessageResource> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await _httpClient.PostAsync(MessagePath(messageSid),
            new FormUrlEncodedContent(form), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    /// <summary>
    /// ListMessage filtered server-side to this application's own sending
    /// number (From) and the requested sent-date range, following every page
    /// so the whole range is covered.
    /// </summary>
    public async Task<IReadOnlyList<TwilioMessageResource>> ListMessagesAsync(
        DateTimeOffset dateSentAfter, DateTimeOffset dateSentBefore, CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageResource>();

        string Format(DateTimeOffset value) =>
            value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var query = string.Join("&", new[]
        {
            ("From", _settings.FromNumber),
            ("DateSent>", Format(dateSentAfter)),
            ("DateSent<", Format(dateSentBefore)),
            ("PageSize", "1000"),
        }.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));

        string? nextUri = MessagesPath + "?" + query;
        while (nextUri != null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            var page = await ReadAsync<TwilioListMessagesResponse>(response, cancellationToken);
            if (page?.Messages != null)
            {
                results.AddRange(page.Messages);
            }
            nextUri = page?.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessageResource> ReadMessageAsync(HttpResponseMessage response,
        CancellationToken cancellationToken, bool expectCreated = false)
    {
        var message = await ReadAsync<TwilioMessageResource>(response, cancellationToken);
        if (message?.Sid == null)
        {
            throw new TextMessageProviderException("Twilio messaging API returned an unexpected payload.");
        }
        return message;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToProviderException(response, content);
        }
        return string.IsNullOrEmpty(content) ? default : JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static TextMessageProviderException ToProviderException(HttpResponseMessage response, string content)
    {
        TwilioErrorResource? error = null;
        try
        {
            error = JsonSerializer.Deserialize<TwilioErrorResource>(content, JsonOptions);
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to the generic message.
        }

        // Message stays free of shopper PII so it is always safe to log;
        // Detail carries the provider's own error text for storage only.
        var safeMessage = $"Twilio messaging API request failed with HTTP {(int)response.StatusCode}" +
                          (error?.Code != null ? $" (Twilio error {error.Code})" : string.Empty);
        return new TextMessageProviderException(safeMessage, error?.Message)
        {
            HttpStatusCode = (int)response.StatusCode,
            ProviderErrorCode = error?.Code
        };
    }

    public static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
