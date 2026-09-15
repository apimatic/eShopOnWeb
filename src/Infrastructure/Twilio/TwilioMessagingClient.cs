using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// A thin, hand-written client for the provider's 2010-04-01 Message resource, built directly against
/// the OpenAPI contract in <c>api-specs/twilio/twilio_api_v2010</c>. Every messaging-API call goes
/// through the configured base address (the <c>Twilio:BaseUrl</c> override when set, otherwise the
/// provider default). Auth is HTTP Basic (AccountSid:AuthToken), configured on the HttpClient.
/// </summary>
public class TwilioMessagingClient
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    private string BaseUrl => _settings.ResolvedMessagingBaseUrl;
    private string MessagesUrl => $"{BaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{BaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    /// <summary>POST /Messages.json — create (send or schedule) a message.</summary>
    public async Task<TwilioMessageResource> CreateAsync(IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessagesUrl, content, ct);
        return await ReadResourceOrThrowAsync(response, ct);
    }

    /// <summary>GET /Messages/{Sid}.json — fetch a message's current record.</summary>
    public async Task<TwilioMessageResource> FetchAsync(string sid, CancellationToken ct)
    {
        using var response = await _http.GetAsync(MessageUrl(sid), ct);
        return await ReadResourceOrThrowAsync(response, ct);
    }

    /// <summary>POST /Messages/{Sid}.json — update a message (redact body or cancel a scheduled one).</summary>
    public async Task<TwilioMessageResource> UpdateAsync(string sid, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessageUrl(sid), content, ct);
        return await ReadResourceOrThrowAsync(response, ct);
    }

    /// <summary>The absolute URL of the first reconciliation page for the given filter.</summary>
    public string BuildListUrl(string fromNumber, string dateSentFromInclusive, string dateSentToInclusive, int pageSize)
    {
        // Ask the provider to filter by this application's own sending number and the date range, rather
        // than filtering a wider answer afterwards. The '>' / '<' in the parameter names are the
        // provider's own range-filter keys (they arrive URL-encoded as %3E / %3C).
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", fromNumber),
            new("DateSent>", dateSentFromInclusive),
            new("DateSent<", dateSentToInclusive),
            new("PageSize", pageSize.ToString())
        };
        using var encoded = new FormUrlEncodedContent(query);
        var qs = encoded.ReadAsStringAsync().GetAwaiter().GetResult();
        return $"{MessagesUrl}?{qs}";
    }

    /// <summary>GET a reconciliation page by absolute URL (first page) or provider-relative next_page_uri.</summary>
    public async Task<TwilioMessageListResponse> GetPageAsync(string url, CancellationToken ct)
    {
        var absolute = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"{BaseUrl}{url}";
        using var response = await _http.GetAsync(absolute, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowFromErrorAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<TwilioMessageListResponse>(stream, TwilioJson.Options, ct)
               ?? new TwilioMessageListResponse();
    }

    private static async Task<TwilioMessageResource> ReadResourceOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            await ThrowFromErrorAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<TwilioMessageResource>(stream, TwilioJson.Options, ct)
               ?? throw new SmsGatewayException("The provider returned an empty response.");
    }

    private static async Task ThrowFromErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        TwilioErrorResponse? error = null;
        try { error = JsonSerializer.Deserialize<TwilioErrorResponse>(raw, TwilioJson.Options); }
        catch { /* body was not the provider's error model */ }

        var message = error?.Message ?? $"The provider returned HTTP {(int)response.StatusCode}.";
        throw new SmsGatewayException(PhoneNumberRedactor.Scrub(message), error?.Code);
    }
}
