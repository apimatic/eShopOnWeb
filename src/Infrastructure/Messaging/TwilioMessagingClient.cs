using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Hand-written client for the Twilio messaging API, built against the authoritative
/// OpenAPI contract in api-specs/twilio/twilio_api_v2010:
///   POST   /2010-04-01/Accounts/{AccountSid}/Messages.json        (CreateMessage; form-urlencoded)
///   GET    /2010-04-01/Accounts/{AccountSid}/Messages.json        (ListMessage; From/DateSent filters, paged)
///   GET    /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json  (FetchMessage)
///   POST   /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json  (UpdateMessage: Status=canceled, Body="" redacts)
/// Auth: HTTP Basic with AccountSid:AuthToken (security scheme accountSid_authToken).
/// </summary>
public class TwilioMessagingClient : ISmsGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio settings are missing. Bind the 'Twilio' configuration section (Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, Twilio:MessagingServiceSid).");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagesUrl => $"{_settings.EffectiveMessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_settings.EffectiveMessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<ProviderMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        // CreateMessage: conditional parameters are (from | messaging_service_sid) and (body | media_url | content_sid).
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        return await PostMessageAsync(MessagesUrl, form, cancellationToken);
    }

    public async Task<ProviderMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default)
    {
        // Scheduled sends are a Messaging Services feature: ScheduleType=fixed + SendAt (ISO 8601),
        // with MessagingServiceSid. From is also supplied so the message goes out from this
        // application's configured sending number (a sender in the service's pool).
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        };

        return await PostMessageAsync(MessagesUrl, form, cancellationToken);
    }

    public async Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage with Status=canceled (message_enum_update_status).
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageUrl(providerMessageSid), form, cancellationToken);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage with an empty Body redacts the message text at the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageUrl(providerMessageSid), form, cancellationToken);
    }

    public async Task<ProviderMessage?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only this application's own sending number's messages
        // (From filter), rather than filtering a wider answer after the fact.
        // DateSent>/DateSent< accept GMT date-times; nudge the bounds outward by a
        // second so the [from, to] range is inclusive.
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(fromUtc.AddSeconds(-1).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}",
            $"{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(toUtc.AddSeconds(1).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}",
            "PageSize=1000"
        });

        var results = new List<ProviderMessage>();
        string? nextUri = $"{MessagesUrl}?{query}";

        // Cover the whole range: follow next_page_uri until the provider says there are no more pages.
        while (nextUri is not null)
        {
            var absolute = nextUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nextUri
                : $"{_settings.EffectiveMessagingBaseUrl}{nextUri}";

            using var response = await _httpClient.GetAsync(absolute, cancellationToken);
            var page = await ReadAsync<TwilioListMessagesResponse>(response, cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(message.ToProviderMessage());
                }
            }
            nextUri = page?.NextPageUri;
        }

        return results;
    }

    private async Task<ProviderMessage> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var resource = await ReadAsync<TwilioMessageResource>(response, cancellationToken);
        return resource!.ToProviderMessage();
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResource? error = null;
            try
            {
                error = JsonSerializer.Deserialize<TwilioErrorResource>(payload, JsonOptions);
            }
            catch (JsonException)
            {
                // Non-JSON error body; fall through to a generic provider exception.
            }

            // The raw provider message may embed the destination number; it is kept on the
            // exception for the caller but must only be logged via ErrorCode.
            _logger.LogWarning("Twilio messaging API call failed with HTTP {StatusCode}, provider error code {ErrorCode}.",
                (int)response.StatusCode, error?.Code);
            throw new ProviderException(error?.Message ?? $"Twilio API error (HTTP {(int)response.StatusCode}).", error?.Code, (int)response.StatusCode);
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }
}
