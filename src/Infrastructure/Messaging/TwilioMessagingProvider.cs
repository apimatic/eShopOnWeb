using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Hand-written Twilio client built against the OpenAPI specifications in
/// api-specs/twilio (twilio_api_v2010 for messaging, twilio_lookups_v2 for number
/// validation). Auth is HTTP Basic with AccountSid:AuthToken per the spec's
/// accountSid_authToken security scheme. The auth token and destination numbers
/// are never logged.
/// </summary>
public class TwilioMessagingProvider : IMessagingProvider, IPhoneNumberLookup
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingProvider> _logger;

    public TwilioMessagingProvider(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessagingProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl.TrimEnd('/');

    private string MessagesUrl => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? scheduleAt = null, CancellationToken cancellationToken = default)
    {
        // CreateMessage: POST /2010-04-01/Accounts/{AccountSid}/Messages.json (form-urlencoded)
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body)
        };

        if (scheduleAt.HasValue)
        {
            // Provider-side scheduling requires a Messaging Service per the spec
            // (ScheduleType=fixed together with SendAt).
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", scheduleAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }
        else
        {
            form.Add(new("From", _settings.FromNumber));
        }

        using var request = NewRequest(HttpMethod.Post, MessagesUrl);
        request.Content = new FormUrlEncodedContent(form);

        var message = await SendAsync<TwilioMessage>(request, cancellationToken);
        _logger.LogInformation("Twilio CreateMessage accepted {MessageSid} with status {Status}", message.Sid, message.Status);
        return ToProviderMessage(message);
    }

    public async Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // FetchMessage: GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json
        using var request = NewRequest(HttpMethod.Get, MessageUrl(messageSid));
        var message = await SendAsync<TwilioMessage>(request, cancellationToken);
        return ToProviderMessage(message);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // ListMessage: GET .../Messages.json — filtered at the source to this
        // application's own sending number (From) and the requested date range.
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", _settings.FromNumber),
            new("DateSent>", from.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            new("DateSent<", to.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            new("PageSize", "1000")
        };

        var results = new List<ProviderMessage>();
        string? nextUri = MessagesUrl + "?" + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        while (nextUri != null)
        {
            var url = nextUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nextUri
                : MessagingBaseUrl + nextUri;
            using var request = NewRequest(HttpMethod.Get, url);
            var page = await SendAsync<TwilioListMessageResponse>(request, cancellationToken);
            results.AddRange(page.Messages.Select(ToProviderMessage));
            nextUri = page.NextPageUri;
        }

        return results;
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage: POST .../Messages/{Sid}.json with Status=canceled
        using var request = NewRequest(HttpMethod.Post, MessageUrl(messageSid));
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Status", "canceled") });
        var message = await SendAsync<TwilioMessage>(request, cancellationToken);
        return ToProviderMessage(message);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage: POST .../Messages/{Sid}.json with Body="" redacts the text at the provider.
        using var request = NewRequest(HttpMethod.Post, MessageUrl(messageSid));
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Body", "") });
        await SendAsync<TwilioMessage>(request, cancellationToken);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookups v2: GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
        // This host is NOT governed by Twilio:BaseUrl (messaging-only override).
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = NewRequest(HttpMethod.Get, url);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, null, new[] { "NOT_FOUND" });
        }

        var lookup = await ReadJsonAsync<TwilioLookupResponse>(response, cancellationToken);
        return new PhoneNumberLookupResult(
            lookup.Valid,
            lookup.Valid ? lookup.PhoneNumber : null,
            lookup.NationalFormat,
            lookup.ValidationErrors ?? new List<string>());
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        // accountSid_authToken: HTTP Basic per the spec's security scheme.
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResponse? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<TwilioErrorResponse>(cancellationToken: cancellationToken);
            }
            catch
            {
                // Non-JSON error body; fall through to a generic exception below.
            }
            throw new TwilioApiException(response.StatusCode, error?.Code,
                error?.Message ?? $"Unexpected {(int)response.StatusCode} response from Twilio.");
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return payload ?? throw new TwilioApiException(response.StatusCode, null, "Empty response body from Twilio.");
    }

    private static ProviderMessage ToProviderMessage(TwilioMessage message)
    {
        return new ProviderMessage(
            message.Sid ?? string.Empty,
            message.Status ?? string.Empty,
            message.ErrorCode,
            message.ErrorMessage,
            ParseRfc2822(message.DateSent),
            message.From,
            message.To);
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        // The spec declares date-time-rfc-2822, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
