using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Talks to Twilio's REST API over HTTP exactly as documented: HTTP Basic auth, form-encoded request
/// bodies, snake_case JSON responses. The messaging calls (send, read, redact, list) go to the
/// configured messaging base address; Lookup lives on its own host and is unaffected by the override.
/// The auth token and shopper destination numbers are never logged.
/// </summary>
public class TwilioSmsGateway : ISmsGateway, ISmsSenderIdentity
{
    private const int MaxReconciliationPages = 100;

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        // One Basic-auth credential pair satisfies every host (messaging and lookup).
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public string FromNumber => _options.FromNumber;

    // ----- Lookup (number validation & canonicalization) -------------------------------------------

    public async Task<PhoneNumberLookup> LookupAsync(string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        // GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
        var url = $"{TwilioOptions.LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawPhoneNumber)}";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SmsGatewayException("Lookup request could not be completed.", inner: ex);
        }

        // A number the provider cannot even parse comes back as 400/404 — that is an unusable number,
        // a normal negative result rather than a fault.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new PhoneNumberLookup(false, null, "The number is not a valid, dialable phone number.");
        }

        await EnsureSuccessAsync(response, "Lookup", cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<LookupResponse>(cancellationToken: cancellationToken);
        if (body is null)
        {
            throw new SmsGatewayException("Lookup returned an unreadable response.");
        }

        if (!body.Valid || string.IsNullOrEmpty(body.PhoneNumber))
        {
            var reason = body.ValidationErrors is { Length: > 0 }
                ? $"The number is not a usable destination ({string.Join(", ", body.ValidationErrors)})."
                : "The number is not a usable destination.";
            return new PhoneNumberLookup(false, null, reason);
        }

        // Store the provider's own canonical E.164 form, not whatever the caller typed.
        return new PhoneNumberLookup(true, body.PhoneNumber, null);
    }

    // ----- Send / schedule -------------------------------------------------------------------------

    public async Task<GatewayMessage> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body,
        };

        if (request.SendAt.HasValue)
        {
            // Scheduling requires a Messaging Service; the app's own number is named as the sender from
            // the pool so the scheduled message stays attributable to it. SendAt is ISO-8601.
            form["MessagingServiceSid"] = _options.MessagingServiceSid;
            form["From"] = _options.FromNumber;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        else
        {
            form["From"] = _options.FromNumber;
        }

        var message = await PostMessageFormAsync(MessagesUrl(), form, "Send", cancellationToken);
        return ToGatewayMessage(message);
    }

    // ----- Read ------------------------------------------------------------------------------------

    public async Task<GatewayMessage> FetchAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        var url = MessageUrl(providerMessageId);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SmsGatewayException("Fetch message request could not be completed.", inner: ex);
        }

        await EnsureSuccessAsync(response, "Fetch", cancellationToken);
        var message = await response.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken: cancellationToken)
                      ?? throw new SmsGatewayException("Fetch returned an unreadable response.");
        return ToGatewayMessage(message);
    }

    // ----- Cancel a scheduled message --------------------------------------------------------------

    public async Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        // POST Messages/{Sid}.json with Status=canceled (the only accepted value).
        await PostMessageFormAsync(MessageUrl(providerMessageId),
            new Dictionary<string, string> { ["Status"] = "canceled" }, "Cancel", cancellationToken);
    }

    // ----- Redact the body (dispose content at the provider) ---------------------------------------

    public async Task RedactBodyAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        // POST Messages/{Sid}.json with Body set to an empty string redacts the text at the provider,
        // while the message record — and its delivery outcome — survives.
        await PostMessageFormAsync(MessageUrl(providerMessageId),
            new Dictionary<string, string> { ["Body"] = string.Empty }, "Redact", cancellationToken);
    }

    // ----- Reconciliation list ---------------------------------------------------------------------

    public async Task<IReadOnlyList<GatewayMessage>> ListSentFromAsync(
        string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider directly for this sender's messages, filtered by From and a coarse DateSent
        // window (the filter is date-granular). The exact [from, to] bound is applied after parsing.
        var lowerDate = from.ToUniversalTime().Date;
        var upperDate = to.ToUniversalTime().Date.AddDays(1); // inclusive of the whole 'to' day

        var query = new StringBuilder();
        query.Append("From=").Append(Uri.EscapeDataString(fromNumber));
        query.Append("&DateSent%3E=").Append(lowerDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        query.Append("&DateSent%3C=").Append(upperDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        query.Append("&PageSize=1000");

        var results = new List<GatewayMessage>();
        var nextRelativeOrAbsolute = $"{MessagesUrl()}?{query}";
        var pages = 0;

        while (!string.IsNullOrEmpty(nextRelativeOrAbsolute) && pages < MaxReconciliationPages)
        {
            pages++;
            var url = nextRelativeOrAbsolute!.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nextRelativeOrAbsolute
                : _options.MessagingBaseUrl + nextRelativeOrAbsolute;

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(url, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new SmsGatewayException("List messages request could not be completed.", inner: ex);
            }

            await EnsureSuccessAsync(response, "List", cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<MessageListResponse>(cancellationToken: cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    var mapped = ToGatewayMessage(message);
                    // Precise bound: keep only messages actually sent within [from, to].
                    if (mapped.DateSent is { } sent && sent >= from && sent <= to)
                    {
                        results.Add(mapped);
                    }
                }
            }

            nextRelativeOrAbsolute = page?.NextPageUri;
        }

        return results;
    }

    // ----- Helpers ---------------------------------------------------------------------------------

    private string MessagesUrl() =>
        $"{_options.MessagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageUrl(string sid) =>
        $"{_options.MessagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private async Task<MessageResponse> PostMessageFormAsync(
        string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            response = await _httpClient.PostAsync(url, content, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SmsGatewayException($"{operation} request could not be completed.", inner: ex);
        }

        await EnsureSuccessAsync(response, operation, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken: cancellationToken)
               ?? throw new SmsGatewayException($"{operation} returned an unreadable response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        int? providerCode = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioError>(cancellationToken: cancellationToken);
            providerCode = error?.Code;
        }
        catch
        {
            // response body was not the standard error envelope; the status code is enough
        }

        // Sanitized message only: HTTP status + provider error code. The provider's own message text
        // can echo the destination number, so it is deliberately not included here.
        throw new SmsGatewayException(
            $"{operation} failed with HTTP {(int)response.StatusCode}" +
            (providerCode.HasValue ? $" (provider code {providerCode})." : "."),
            httpStatusCode: (int)response.StatusCode,
            providerErrorCode: providerCode);
    }

    private static GatewayMessage ToGatewayMessage(MessageResponse m) =>
        new(m.Sid ?? string.Empty, m.Status ?? string.Empty, m.ErrorCode, m.To, m.From, ParseTwilioDate(m.DateSent));

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // The /2010-04-01 host returns RFC 2822 (e.g. "Thu, 24 Aug 2023 05:01:45 +0000").
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    // ----- Response DTOs (snake_case) --------------------------------------------------------------

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public List<MessageResponse>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("status")] public int? Status { get; set; }
    }
}
