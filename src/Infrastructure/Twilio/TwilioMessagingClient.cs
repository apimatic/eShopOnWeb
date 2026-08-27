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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for Twilio's messaging API, built to the OpenAPI contract in
/// api-specs/twilio/twilio_api_v2010 (operations CreateMessage, FetchMessage,
/// ListMessage, UpdateMessage). Auth is HTTP Basic with AccountSid:AuthToken per the
/// spec's accountSid_authToken security scheme. The auth token is never logged.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _settings = settings.Value;
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio settings are missing. Provide Twilio:AccountSid and Twilio:AuthToken via user-secrets or environment variables.");
        }

        // Twilio:BaseUrl, when set, is used verbatim as the base address for every
        // messaging-API call instead of the provider's default.
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl!;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<TwilioMessageInfo> SendMessageAsync(string to, string body, DateTimeOffset? sendAtUtc = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body)
        };

        if (sendAtUtc.HasValue)
        {
            // Message scheduling requires a Messaging Service: the message is queued
            // with the provider (ScheduleType=fixed, SendAt in ISO 8601) and Twilio
            // sends it at that time — nothing is held in this application.
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAtUtc.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }
        else
        {
            form.Add(new("From", _settings.FromNumber));
        }

        using var response = await _httpClient.PostAsync(MessagesPath(), new FormUrlEncodedContent(form), cancellationToken);
        var resource = await ReadAsync<TwilioMessageResource>(response, cancellationToken);
        return resource.ToModel();
    }

    public async Task<TwilioMessageInfo> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessagePath(messageSid), cancellationToken);
        var resource = await ReadAsync<TwilioMessageResource>(response, cancellationToken);
        return resource.ToModel();
    }

    public async Task<IReadOnlyList<TwilioMessageInfo>> ListMessagesAsync(string fromNumber, DateTimeOffset? dateSentAfter, DateTimeOffset? dateSentBefore, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this number's messages (From filter server-side), so
        // traffic on the account that is not this application's never enters the report.
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", fromNumber),
            new("PageSize", "1000")
        };
        if (dateSentAfter.HasValue)
        {
            query.Add(new("DateSent>", FormatListDate(dateSentAfter.Value)));
        }
        if (dateSentBefore.HasValue)
        {
            query.Add(new("DateSent<", FormatListDate(dateSentBefore.Value)));
        }

        var results = new List<TwilioMessageInfo>();
        string? nextUri = MessagesPath() + "?" + BuildQuery(query);

        // Cover the whole range: follow the provider's pagination to the end.
        while (nextUri is not null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            var page = await ReadAsync<TwilioListMessageResponse>(response, cancellationToken);
            if (page.Messages is not null)
            {
                results.AddRange(page.Messages.Select(m => m.ToModel()));
            }
            nextUri = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri!.TrimStart('/');
        }

        return results;
    }

    public async Task<TwilioMessageInfo> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await _httpClient.PostAsync(MessagePath(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        var resource = await ReadAsync<TwilioMessageResource>(response, cancellationToken);
        return resource.ToModel();
    }

    public async Task<TwilioMessageInfo> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await _httpClient.PostAsync(MessagePath(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        var resource = await ReadAsync<TwilioMessageResource>(response, cancellationToken);
        return resource.ToModel();
    }

    private string MessagesPath() => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    // The ListMessage DateSent filters accept GMT date-times, e.g. "2019-06-11 22:05:25.000".
    private static string FormatListDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join("&", parameters.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResource? error = null;
            try
            {
                error = JsonSerializer.Deserialize<TwilioErrorResource>(content, TwilioJson.Options);
            }
            catch (JsonException)
            {
                // fall through to a generic error below
            }

            throw new TwilioApiException((int)response.StatusCode, error?.Code,
                error?.Message ?? "Unexpected response from Twilio.");
        }

        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(content, TwilioJson.Options)
            ?? throw new TwilioApiException((int)response.StatusCode, null, "Empty response body from Twilio.");
    }
}
