using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written client for the Twilio messaging API, built against the OpenAPI
/// specification in api-specs/twilio/twilio_api_v2010 (Messages resource).
/// Auth: HTTP Basic with AccountSid:AuthToken per the spec's accountSid_authToken scheme.
/// </summary>
public class TwilioMessagingClient : IMessagingClient
{
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;
    private readonly string _baseUrl;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio settings are missing. Bind the 'Twilio' section (AccountSid, AuthToken, FromNumber) from user-secrets or environment variables.");
        }

        _baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        // The auth token is applied per request and never logged.
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
    }

    public string FromNumber => _settings.FromNumber;

    private string AccountPath => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}";

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        // CreateMessage: POST /2010-04-01/Accounts/{AccountSid}/Messages.json (form-urlencoded)
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("From", _settings.FromNumber)
        };

        if (sendAt.HasValue)
        {
            // Provider-side scheduling requires a Messaging Service per the spec (ScheduleType/SendAt).
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule messages with the provider.");
            }
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid!));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
        }

        using var response = await _httpClient.PostAsync($"{AccountPath}/Messages.json",
            new FormUrlEncodedContent(form), cancellationToken);

        var message = await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio message {MessageSid} created with status {Status}.", message.Sid, message.Status);
        return message;
    }

    public async Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // FetchMessage: GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json
        using var response = await _httpClient.GetAsync($"{AccountPath}/Messages/{messageSid}.json", cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default) =>
        // UpdateMessage with Status=canceled (only valid for not-yet-sent messages)
        UpdateMessageAsync(messageSid, new KeyValuePair<string, string>("Status", "canceled"), cancellationToken);

    public Task<ProviderMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default) =>
        // UpdateMessage with an empty Body redacts the message text at the provider
        UpdateMessageAsync(messageSid, new KeyValuePair<string, string>("Body", string.Empty), cancellationToken);

    private async Task<ProviderMessage> UpdateMessageAsync(string messageSid, KeyValuePair<string, string> field, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"{AccountPath}/Messages/{messageSid}.json",
            new FormUrlEncodedContent(new[] { field }), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string from, DateTimeOffset? dateSentAfter, DateTimeOffset? dateSentBefore, CancellationToken cancellationToken = default)
    {
        // ListMessage: GET Messages.json filtered by sender (From) and sent-date range, paged via next_page_uri.
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", from),
            new("PageSize", "1000")
        };
        if (dateSentAfter.HasValue)
        {
            query.Add(new("DateSent>", dateSentAfter.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
        }
        if (dateSentBefore.HasValue)
        {
            query.Add(new("DateSent<", dateSentBefore.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
        }

        var results = new List<ProviderMessage>();
        var url = $"{AccountPath}/Messages.json?{await new FormUrlEncodedContent(query).ReadAsStringAsync(cancellationToken)}";

        while (url is not null)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<TwilioListMessagesResponse>(cancellationToken: cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(message.ToProviderMessage());
                }
            }
            url = string.IsNullOrEmpty(page?.NextPageUri) ? null : _baseUrl + page!.NextPageUri;
        }

        return results;
    }

    private static async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var dto = await response.Content.ReadFromJsonAsync<TwilioMessageDto>(cancellationToken: cancellationToken);
        return dto?.ToProviderMessage()
            ?? throw new TwilioApiException(response.StatusCode, null, "Empty response body from the provider.", null);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        TwilioErrorDto? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<TwilioErrorDto>(cancellationToken: cancellationToken);
        }
        catch
        {
            // Fall through to a generic error below.
        }

        throw new TwilioApiException(response.StatusCode, error?.Code,
            error?.Message ?? $"Provider returned {(int)response.StatusCode}.", error?.MoreInfo);
    }
}
