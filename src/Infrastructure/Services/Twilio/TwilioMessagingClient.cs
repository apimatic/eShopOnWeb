using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written client for the Twilio messaging API (2010-04-01 Messages resource), built
/// directly against the api-specs contract: paths, form fields, query filters, the basic-auth
/// scheme and the JSON message model all come from the spec. The optional <c>Twilio:BaseUrl</c>
/// overrides the messaging base address; other provider APIs are unaffected.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _baseUrl;
    private readonly string _messagesPath;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl!.TrimEnd('/');
        _messagesPath = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public Task<ProviderMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return PostFormAsync(_baseUrl + _messagesPath, form, cancellationToken);
    }

    public Task<ProviderMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return PostFormAsync(_baseUrl + _messagesPath, form, cancellationToken);
    }

    public Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        return GetMessageAsync(url, cancellationToken);
    }

    public Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return PostFormAsync(url, form, cancellationToken);
    }

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        // Redaction: update the body to an empty string (per the api-specs UpdateMessageRequest).
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        return PostFormAsync(url, form, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The sending-number filter is applied by the provider (From=), not after the fact.
        var fromIso = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Query keys "DateSent>" / "DateSent<" are the provider's inequality filters (>= / <=).
        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={Uri.EscapeDataString(fromIso)}" +
                    $"&DateSent%3C={Uri.EscapeDataString(toIso)}" +
                    "&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? nextUrl = $"{_baseUrl}{_messagesPath}?{query}";

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<TwilioListMessagesResponse>(json);
            if (page is null)
            {
                break;
            }

            foreach (var message in page.Messages)
            {
                results.Add(Map(message));
            }

            nextUrl = string.IsNullOrEmpty(page.NextPageUri) ? null : _baseUrl + page.NextPageUri;
        }

        return results;
    }

    // ----- transport -----------------------------------------------------------------------

    private async Task<ProviderMessage> PostFormAsync(string url, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = JsonSerializer.Deserialize<TwilioMessageResource>(json)
                      ?? throw new TwilioApiException(response.StatusCode, null, "Empty response from provider.");
        return Map(message);
    }

    private async Task<ProviderMessage> GetMessageAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = JsonSerializer.Deserialize<TwilioMessageResource>(json)
                      ?? throw new TwilioApiException(response.StatusCode, null, "Empty response from provider.");
        return Map(message);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? code = null;
        string? message = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var error = JsonSerializer.Deserialize<TwilioErrorResponse>(body);
                code = error?.Code;
                message = error?.Message;
            }
        }
        catch
        {
            // Fall through with whatever we have; never surface a phone number.
        }

        throw new TwilioApiException(response.StatusCode, code, message ?? response.ReasonPhrase);
    }

    private static ProviderMessage Map(TwilioMessageResource m) => new(
        m.Sid,
        m.Status,
        m.ErrorCode,
        m.ErrorMessage,
        m.From,
        m.To,
        m.Body,
        ParseDate(m.DateSent),
        ParseDate(m.DateCreated));

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Twilio serialises dates in RFC 2822, e.g. "Fri, 24 May 2019 17:44:50 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
