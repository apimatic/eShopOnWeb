using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// The Twilio-backed <see cref="ISmsProvider"/>. This is the only type that touches the Twilio
/// SDK; everything above it works in provider-agnostic domain terms. Send/schedule/cancel never
/// throw for a provider-side failure — they translate it into an outcome so a failed message can
/// never fail the underlying order operation. Number validation, redaction, fetch and
/// reconciliation surface faults as <see cref="SmsProviderException"/> because their callers must
/// know they did not happen.
/// </summary>
public sealed class TwilioSmsProvider : ISmsProvider
{
    // Per-attempt Timeout on the client bounds one attempt; these bound the whole call (retries
    // included), the only thing the caller perceives. POST writes are not retried by the SDK, so
    // the single-call budget is tight; the reconciliation walk gets a larger budget.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ListBudget = TimeSpan.FromSeconds(90);
    private const int MaxReconciliationPages = 50;
    private const long ListPageSize = 200;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioSmsProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidateAsync(string rawNumber, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            // Lookups is on its own host (Default4) and is deliberately NOT governed by Twilio:BaseUrl.
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null,
                ct: cts.Token);

            var valid = response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber);
            return new PhoneValidationResult(valid, valid ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex)
        {
            // A number the provider cannot look up (invalid / not a real destination) is a rejection,
            // not a provider fault. Everything else (auth, throttling, 5xx) the caller must hear about.
            var status = (int)ex.Error.StatusCode;
            if (status == 404 || status == 400)
            {
                return new PhoneValidationResult(false, null);
            }

            throw ProviderFault("validate a phone number", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw ProviderFault("validate a phone number", null, ex);
        }
    }

    public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct) =>
        CreateMessageAsync(to, body, scheduleAt: null, ct);

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        CreateMessageAsync(to, body, scheduleAt: sendAt, ct);

    private async Task<SmsSendResult> CreateMessageAsync(string to, string body, DateTimeOffset? scheduleAt, CancellationToken ct)
    {
        var scheduled = scheduleAt.HasValue;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            var response = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: to,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null,
                // Scheduling requires a Messaging Service + fixed schedule type + send-at, and no From.
                scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
                sendAt: scheduleAt,
                sendAsMms: null, contentVariables: null, riskCheck: null,
                from: scheduled ? null : _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: scheduled ? _settings.MessagingServiceSid : null,
                body: body,
                mediaUrl: null, contentSid: null,
                ct: cts.Token);

            return new SmsSendResult
            {
                Outcome = SmsSendOutcome.Accepted,
                ProviderSid = response.Sid,
                Status = response.Status?.Value,
                DateSent = ParseProviderDate(response.DateSent)
            };
        }
        catch (SdkException<RawError> ex)
        {
            // The provider explicitly rejected the request — a definite failure, not an unknown.
            var (code, message) = ReadTwilioError(ex.Error);
            _logger.LogWarning("Twilio rejected a message create (scheduled={Scheduled}) with HTTP {Status}, code {Code}.",
                scheduled, (int)ex.Error.StatusCode, code);
            return new SmsSendResult
            {
                Outcome = SmsSendOutcome.Rejected,
                ErrorCode = code,
                ErrorMessage = message ?? $"HTTP {(int)ex.Error.StatusCode}"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Transport failed (or a 2xx body could not be read): the message may have been created,
            // so the outcome is UNKNOWN and must be reconciled, never recorded as a failure.
            _logger.LogWarning(ex, "Transport failure creating a message (scheduled={Scheduled}); outcome unknown.", scheduled);
            return new SmsSendResult { Outcome = SmsSendOutcome.Unknown, ErrorMessage = "provider unreachable" };
        }
    }

    public async Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            var response = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: cts.Token);

            return new SmsSendResult
            {
                Outcome = SmsSendOutcome.Accepted,
                ProviderSid = response.Sid,
                Status = response.Status?.Value
            };
        }
        catch (SdkException<RawError> ex)
        {
            var (code, message) = ReadTwilioError(ex.Error);
            _logger.LogWarning("Twilio rejected cancelling a scheduled message with HTTP {Status}, code {Code}.",
                (int)ex.Error.StatusCode, code);
            return new SmsSendResult
            {
                Outcome = SmsSendOutcome.Rejected,
                ErrorCode = code,
                ErrorMessage = message ?? $"HTTP {(int)ex.Error.StatusCode}"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Transport failure cancelling a scheduled message; outcome unknown.");
            return new SmsSendResult { Outcome = SmsSendOutcome.Unknown, ErrorMessage = "provider unreachable" };
        }
    }

    public async Task<ProviderMessage?> FetchAsync(string providerSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            var response = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                ct: cts.Token);

            return ToProviderMessage(response);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFault("fetch a message", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw ProviderFault("fetch a message", null, ex);
        }
    }

    public async Task RedactContentAsync(string providerSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            // Twilio message-body redaction: POST an empty Body to the message resource. This removes
            // the retrievable text while the record (SID, status) and its outcome survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: string.Empty,
                status: null,
                ct: cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFault("dispose of a message's content", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw ProviderFault("dispose of a message's content", null, ex);
        }
    }

    public async Task<ProviderMessageListResult> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ListBudget);

        var messages = new List<ProviderMessage>();
        int? page = null;
        string? pageToken = null;
        var pages = 0;
        var truncated = false;

        try
        {
            while (true)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    // Ask the provider only for THIS application's sending number — not filtered after
                    // the fact — because the account carries traffic that is not this application's.
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,          // DateSent<  (upper bound)
                    dateSentQueryQuery: from,   // DateSent>  (lower bound)
                    pageSize: ListPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: cts.Token);

                if (response.Messages is not null)
                {
                    foreach (var m in response.Messages)
                    {
                        messages.Add(ToProviderMessage(m));
                    }
                }

                pages++;

                var next = ParseNextPage(response.NextPageUri);
                if (next is null)
                {
                    break; // provider signalled the end
                }

                if (pages >= MaxReconciliationPages)
                {
                    truncated = true; // bound that does not depend on the provider's cooperation
                    _logger.LogWarning("Reconciliation page walk hit the {Cap}-page cap; result is truncated.", MaxReconciliationPages);
                    break;
                }

                (page, pageToken) = next.Value;
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFault("list messages for reconciliation", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw ProviderFault("list messages for reconciliation", null, ex);
        }

        return new ProviderMessageListResult
        {
            Messages = messages,
            Truncated = truncated,
            PagesFetched = pages
        };
    }

    public async Task<ProviderMessage?> FindRecentAsync(string to, DateTimeOffset sentAfter, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            var response = await _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: to,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: null,
                dateSentQueryQuery: sentAfter,
                pageSize: 20,
                page: null,
                pageToken: null,
                ct: cts.Token);

            if (response.Messages is null || response.Messages.Count == 0)
            {
                return null;
            }

            // Twilio lists newest first; the first is the most recent match.
            return ToProviderMessage(response.Messages[0]);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFault("search recent messages", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw ProviderFault("search recent messages", null, ex);
        }
    }

    private static ProviderMessage ToProviderMessage(TwilioSdk.Models.ApiV2010AccountMessage m) => new()
    {
        Sid = m.Sid,
        Status = m.Status?.Value,
        To = m.To,
        From = m.From,
        ErrorCode = m.ErrorCode,
        ErrorMessage = m.ErrorMessage,
        DateSent = ParseProviderDate(m.DateSent)
    };

    private SmsProviderException ProviderFault(string action, HttpStatusCode? status, Exception inner)
    {
        // Never echo the recipient number, message body, or any credential.
        _logger.LogError(inner, "Twilio provider fault while trying to {Action} (status {Status}).", action, status);
        return new SmsProviderException($"The SMS provider could not {action}.", status is null ? null : (int)status, inner);
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Parse the Page and PageToken query values out of Twilio's next_page_uri.</summary>
    private static (int? Page, string? PageToken)? ParseNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0 || queryStart == nextPageUri.Length - 1)
        {
            return null;
        }

        int? page = null;
        string? pageToken = null;

        var query = nextPageUri.Substring(queryStart + 1);
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair.Substring(0, eq));
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));

            if (string.Equals(key, "Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
            {
                page = p;
            }
            else if (string.Equals(key, "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
            }
        }

        return (page is null && pageToken is null) ? null : (page, pageToken);
    }

    private (int? Code, string? Message) ReadTwilioError(RawError raw)
    {
        // The error body may not be JSON (a gateway/proxy can answer with HTML/text), so guard the parse.
        try
        {
            var body = raw.ReadAsJson<TwilioErrorBody>();
            return (body?.Code, body?.Message);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, null);
        }
    }

    private sealed record TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}
