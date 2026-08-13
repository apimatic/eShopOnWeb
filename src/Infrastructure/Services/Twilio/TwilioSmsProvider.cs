using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// <see cref="ISmsProvider"/> implemented directly against Twilio's OpenAPI contract:
/// <list type="bullet">
/// <item>Lookups v2 <c>GET /v2/PhoneNumbers/{PhoneNumber}</c> for validation + canonical E.164.</item>
/// <item>Account v2010 <c>Messages</c> resource for send, schedule, fetch, cancel, redact and list.</item>
/// </list>
/// Auth is HTTP Basic (Account SID / Auth Token) configured on the injected <see cref="HttpClient"/>.
/// No pre-built Twilio SDK is used. The client's default request logging is removed at registration so
/// destination numbers (which appear in the Lookups URL) and credentials are never written to logs.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;

    public TwilioSmsProvider(HttpClient http, IOptions<TwilioSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    private string MessagesCollectionUri =>
        $"{_settings.MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstanceUri(string sid) =>
        $"{_settings.MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    // ---------------------------------------------------------------------------- Lookups v2

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var uri = $"{TwilioSettings.LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _http.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider does not recognise the number as a real, dialable destination.
            return new PhoneLookupResult { IsValid = false, ValidationErrors = new[] { "NOT_FOUND" } };
        }

        if (!response.IsSuccessStatusCode)
            throw await BuildExceptionAsync("phone lookup", response, cancellationToken);

        var dto = await ReadJsonAsync<TwilioLookupDto>(response, cancellationToken);
        return new PhoneLookupResult
        {
            IsValid = dto.Valid,
            CanonicalNumber = dto.Valid ? dto.PhoneNumber : null,
            ValidationErrors = dto.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>()
        };
    }

    // ---------------------------------------------------------------- Messages: create / send

    public async Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(MessagesCollectionUri, form, "send message", cancellationToken);
    }

    public async Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Service capability: MessagingServiceSid + ScheduleType=fixed + SendAt.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(MessagesCollectionUri, form, "schedule message", cancellationToken);
    }

    // ---------------------------------------------------------------- Messages: fetch / update

    public async Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessageInstanceUri(providerMessageSid), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await BuildExceptionAsync("fetch message", response, cancellationToken);

        var dto = await ReadJsonAsync<TwilioMessageDto>(response, cancellationToken);
        return ToProviderMessage(dto);
    }

    public async Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageInstanceUri(providerMessageSid), form, "cancel scheduled message", cancellationToken);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Redact by updating the message body to an empty string, per the contract.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessageInstanceUri(providerMessageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await BuildExceptionAsync("redact message", response, cancellationToken);
    }

    // ---------------------------------------------------------------- Messages: list (paged)

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for messages sent from our configured number, bounded by date, and page fully.
        // The date filter uses date-granular bounds (inclusive on both days); the exact date-time window
        // is applied by the caller.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Parameter names include the inequality (DateSent> / DateSent<), url-encoded.
        var query =
            $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromDate)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toDate)}" +
            "&PageSize=1000";

        var next = MessagesCollectionUri + query;
        var results = new List<ProviderMessage>();
        var safetyPageCap = 1000;

        while (!string.IsNullOrEmpty(next) && safetyPageCap-- > 0)
        {
            using var response = await _http.GetAsync(next, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw await BuildExceptionAsync("list messages", response, cancellationToken);

            var page = await ReadJsonAsync<TwilioListMessagesDto>(response, cancellationToken);
            if (page.Messages is not null)
                foreach (var m in page.Messages)
                    results.Add(ToProviderMessage(m));

            next = ResolveNextPageUri(page.NextPageUri);
        }

        return results;
    }

    // ---------------------------------------------------------------- helpers

    private string? ResolveNextPageUri(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
            return null;
        if (nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return nextPageUri;
        // next_page_uri is a path relative to the messaging host; honour any configured base-URL override.
        return _settings.MessagingBaseUrl + nextPageUri;
    }

    private async Task<ProviderMessage> PostMessageAsync(string uri, IDictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(uri, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await BuildExceptionAsync(operation, response, cancellationToken);

        var dto = await ReadJsonAsync<TwilioMessageDto>(response, cancellationToken);
        return ToProviderMessage(dto);
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageDto dto) => new()
    {
        Sid = dto.Sid,
        Status = dto.Status,
        ErrorCode = dto.ErrorCode,
        ErrorMessage = dto.ErrorMessage,
        To = dto.To,
        From = dto.From,
        Body = dto.Body,
        DateCreated = ParseDate(dto.DateCreated),
        DateSent = ParseDate(dto.DateSent),
        MessagingServiceSid = dto.MessagingServiceSid
    };

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        if (value is null)
            throw new TwilioApiException("response parsing", (int)response.StatusCode, null, "empty or unparsable response body");
        return value;
    }

    private static async Task<TwilioApiException> BuildExceptionAsync(string operation, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int? code = null;
        string? moreInfo = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioErrorDto>(cancellationToken);
            if (error is not null)
            {
                code = error.Code;
                moreInfo = error.MoreInfo; // a generic documentation URL, safe to surface
            }
        }
        catch
        {
            // Non-JSON error body; fall back to the HTTP status only. Never surface raw body text,
            // which could contain the destination number.
        }

        return new TwilioApiException(operation, (int)response.StatusCode, code, moreInfo);
    }
}
