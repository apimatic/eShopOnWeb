using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Domain-facing wrapper over the Twilio messaging + lookup APIs. Every call is bounded by a total
/// timeout (a CancellationToken deadline — the SDK's own Timeout is per-attempt) and every provider
/// or transport failure is translated to <see cref="ProviderMessagingException"/> here, so callers
/// deal with one failure type. The shopper's number and message body are never logged.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(TwilioSdkClient client, TwilioSettings settings, IAppLogger<TwilioMessagingClient> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default) =>
        ExecuteAsync("lookup", async token =>
        {
            // Lookup resolves through server group Default4 (lookups.twilio.com) and is NOT governed
            // by Twilio:BaseUrl. Pass only the number; all optional lookup fields are omitted.
            LookupResponse resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: token);

            return new PhoneValidationResult(resp.Valid == true, resp.PhoneNumber, resp.CountryCode);
        }, ct);

    public Task<SentMessage> SendAsync(string toE164, string body, CancellationToken ct = default) =>
        ExecuteAsync("send", async token =>
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null,
                contentRetention: null,
                addressRetention: MessageEnumAddressRetention.Obfuscate, // keep the number out of provider-side logs
                smartEncoded: null, persistentAction: null, trafficType: null, shortenUrls: null,
                scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null, riskCheck: null,
                from: _settings.FromNumber, // attributable to our sender for reconciliation
                fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: token);

            return ToSentMessage(msg);
        }, ct);

    public Task<SentMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default) =>
        ExecuteAsync("schedule", async token =>
        {
            // Scheduling is Messaging-Service-only: set scheduleType=fixed + sendAt + messagingServiceSid,
            // and DO NOT set 'from' (the two are mutually exclusive for scheduled sends).
            ApiV2010AccountMessage msg = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null,
                contentRetention: null,
                addressRetention: MessageEnumAddressRetention.Obfuscate,
                smartEncoded: null, persistentAction: null, trafficType: null, shortenUrls: null,
                scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null,
                from: null, fallbackFrom: null, messagingServiceSid: _settings.MessagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: token);

            return ToSentMessage(msg);
        }, ct);

    public Task<SentMessage> CancelScheduledAsync(string providerSid, CancellationToken ct = default) =>
        ExecuteAsync("cancel", async token =>
        {
            // UpdateMessage with status=canceled cancels a not-yet-sent (scheduled) message.
            ApiV2010AccountMessage msg = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: token);

            return ToSentMessage(msg);
        }, ct);

    public Task<SentMessage> FetchAsync(string providerSid, CancellationToken ct = default) =>
        ExecuteAsync("fetch", async token =>
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                ct: token);

            return ToSentMessage(msg);
        }, ct);

    public Task RedactAsync(string providerSid, CancellationToken ct = default) =>
        ExecuteAsync("redact", async token =>
        {
            // UpdateMessage with an empty body redacts the message text at the provider; the record
            // and its status survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: string.Empty,
                status: null,
                ct: token);

            return true;
        }, ct);

    public Task<ProviderMessagePage> ListSentFromNumberAsync(DateTimeOffset from, DateTimeOffset to, int maxPages, CancellationToken ct = default) =>
        ExecuteAsync("list", async token =>
        {
            const long pageSize = 1000;
            var collected = new List<ProviderMessage>();
            var truncated = false;

            int page = 0;
            while (true)
            {
                if (page >= maxPages)
                {
                    truncated = true;
                    _logger.LogWarning("Reconciliation listing hit the page cap of {MaxPages}; result is truncated.", maxPages);
                    break;
                }

                // Ask the provider for OUR sender's messages in the range:
                //   From ← from(number), DateSent< ← dateSentQuery(upper=to), DateSent> ← dateSentQueryQuery(lower=from-date)
                ListMessageResponse resp = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: pageSize,
                    page: page,
                    pageToken: null,
                    ct: token);

                var messages = resp.Messages;
                if (messages is not null)
                {
                    foreach (var m in messages)
                    {
                        collected.Add(new ProviderMessage(
                            m.Sid, m.Status?.Value, m.To, m.From, ParseDate(m.DateSent)));
                    }
                }

                var count = messages?.Count ?? 0;
                if (count < pageSize || string.IsNullOrEmpty(resp.NextPageUri))
                    break; // provider signalled the end

                page++;
            }

            return new ProviderMessagePage(collected, truncated);
        }, ct);

    private static SentMessage ToSentMessage(ApiV2010AccountMessage msg) =>
        new(msg.Sid, msg.Status?.Value, msg.ErrorCode, msg.ErrorMessage, ParseDate(msg.DateSent));

    private static DateTimeOffset? ParseDate(string? rfc2822) =>
        DateTimeOffset.TryParse(rfc2822, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;

    /// <summary>
    /// Bounds one SDK call with a total-timeout deadline (linked to the caller's token) and
    /// translates every provider/transport/parse failure to <see cref="ProviderMessagingException"/>.
    /// Never logs the destination number or message body.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(string op, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            // Case B: the error model IS RawError; it carries the status. Do not log the body — a
            // provider error body for a message can echo the destination number.
            _logger.LogWarning("Twilio {Operation} failed: HTTP {StatusCode}.", op, (int)ex.Error.StatusCode);
            throw new ProviderMessagingException($"Twilio {op} failed (HTTP {(int)ex.Error.StatusCode}).", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model, surfacing from deserialization.
            _logger.LogWarning("Twilio {Operation} returned an unreadable response.", op);
            throw new ProviderMessagingException($"Twilio {op} returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // Transport failure, or our own per-call budget elapsing (not the caller cancelling).
            _logger.LogWarning("Twilio {Operation} could not reach the provider.", op);
            throw new ProviderMessagingException($"Twilio {op}: provider unreachable or timed out.", ex);
        }
    }
}
