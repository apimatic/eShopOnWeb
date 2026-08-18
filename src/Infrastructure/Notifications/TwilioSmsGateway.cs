using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// The Twilio-backed implementation of <see cref="ISmsGateway"/>. This is the only type in the
/// solution that touches the Twilio SDK; everything else works against the abstraction.
///
/// The error boundary lives here: every SDK failure — a provider API error, a malformed body, or a
/// connection failure — is converted to <see cref="SmsGatewayException"/> in one place, so callers
/// have a single failure type. Destination numbers are never logged, and any provider text that
/// could echo a number back is scrubbed before it leaves this class.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    // A whole-call budget; the client's per-attempt timeout (set at registration) bounds one attempt.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Matches phone-number-like runs so provider error text can't leak a destination number.
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\-\s().]{6,}\d", RegexOptions.Compiled);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioSdkClient client, TwilioSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct) =>
        ExecuteAsync("phone-number lookup", async token =>
        {
            LookupResponse resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null,
                ct: token);

            if (resp.Valid == true && !string.IsNullOrWhiteSpace(resp.PhoneNumber))
                return new PhoneValidationResult(true, resp.PhoneNumber, null);

            var reason = resp.ValidationErrors is { Count: > 0 }
                ? string.Join(", ", resp.ValidationErrors.Select(v => v.Value))
                : "The number is not a usable destination.";
            return new PhoneValidationResult(false, null, reason);
        }, ct);

    public Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken ct) =>
        ExecuteAsync("send", async token =>
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null,
                contentVariables: null, riskCheck: null,
                from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: token);
            return ToDispatchResult(msg);
        }, ct);

    public Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        ExecuteAsync("schedule", async token =>
        {
            // Scheduling requires a Messaging Service + a Fixed schedule type + the send time.
            ApiV2010AccountMessage msg = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null,
                from: null, fallbackFrom: null, messagingServiceSid: _settings.MessagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: token);
            return ToDispatchResult(msg);
        }, ct);

    public Task<SmsDispatchResult> CancelScheduledAsync(string messageSid, CancellationToken ct) =>
        ExecuteAsync("cancel-scheduled", async token =>
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: token);
            return ToDispatchResult(msg);
        }, ct);

    public Task<SmsDispatchResult> FetchAsync(string messageSid, CancellationToken ct) =>
        ExecuteAsync("fetch", async token =>
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                ct: token);
            return ToDispatchResult(msg);
        }, ct);

    public Task RedactAsync(string messageSid, CancellationToken ct) =>
        ExecuteAsync("redact", async token =>
        {
            // Empty body redacts the content at the provider while keeping the message record.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: string.Empty,
                status: null,
                ct: token);
            return true;
        }, ct);

    public Task<IReadOnlyList<ProviderMessageSummary>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        ExecuteAsync<IReadOnlyList<ProviderMessageSummary>>("reconciliation list", async token =>
        {
            const int MaxPages = 100;   // provider-independent backstop against an unbounded page loop
            const long PageSize = 200;

            var results = new List<ProviderMessageSummary>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int page = 0;

            while (page < MaxPages)
            {
                ListMessageResponse resp = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,      // ask the provider only for our sending number's traffic
                    dateSent: null,
                    dateSentQuery: to,               // wire DateSent< — upper bound (on/before "to")
                    dateSentQueryQuery: from,        // wire DateSent> — lower bound (on/after "from")
                    pageSize: PageSize,
                    page: page,
                    pageToken: null,
                    ct: token);

                var messages = resp.Messages;
                if (messages is null || messages.Count == 0)
                    break;

                foreach (var m in messages)
                {
                    if (m.Sid is not null && !seen.Add(m.Sid))
                        continue;   // de-dupe by SID (boundary widening / overlap safety)
                    results.Add(new ProviderMessageSummary(
                        m.Sid, m.To, m.From, m.Status?.Value, ParseDate(m.DateSent), m.Body));
                }

                if (string.IsNullOrEmpty(resp.NextPageUri))
                    break;
                page++;
            }

            return results;
        }, ct);

    private SmsDispatchResult ToDispatchResult(ApiV2010AccountMessage msg) =>
        new(msg.Sid, msg.Status?.Value, msg.ErrorCode, Scrub(msg.ErrorMessage));

    /// <summary>
    /// The single error boundary. Applies a whole-call budget and converts every SDK failure to
    /// <see cref="SmsGatewayException"/>: an API error carries the provider status; a malformed body
    /// or a transport failure carries none. No message here contains a destination number.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsGatewayException(
                $"The SMS provider rejected the {operation} request (HTTP {(int)ex.Error.StatusCode}).",
                ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model, or a non-2xx body that didn't match its
            // error shape (which destroys the status). Either way: a response we couldn't process.
            throw new SmsGatewayException(
                $"The SMS provider returned a {operation} response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsGatewayException($"The SMS provider was unreachable during {operation}.", null, ex);
        }
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>Removes any phone-number-like run from provider text so a destination number can't leak.</summary>
    private static string? Scrub(string? text) =>
        text is null ? null : PhoneLike.Replace(text, "[redacted]");
}
