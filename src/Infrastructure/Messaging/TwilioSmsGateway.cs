using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Talks to Twilio's messaging + lookup APIs through the AsadAli.TwilioSdk client. All facts about a
/// message are obtained by asking the provider (there is no callback into this app). Provider and
/// transport failures are translated into <see cref="SmsGatewayException"/>; no secret or shopper
/// number is ever logged.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    // Twilio serves up to 1000 records per page.
    private const long PageSize = 1000;
    // A hard backstop so reconciliation can never loop forever on a misbehaving pager.
    private const int MaxPages = 500;

    // Per-call ceiling: the whole call (across the SDK's own per-attempt retries) is bounded by this.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null, ct: token), cancellationToken);

            // Treat "not valid" OR a null/empty canonical number as "not a usable destination".
            var isValid = response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber);
            return new PhoneValidationResult(isValid, isValid ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // A number the provider cannot parse comes back 404 on Lookup — reject it, don't treat as an outage.
            return new PhoneValidationResult(false, null);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "validate the phone number", cancellationToken);
        }
    }

    public async Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(token => _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null,
                contentVariables: null, riskCheck: null,
                from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: token), cancellationToken);

            return ToSendResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "send the message", cancellationToken);
        }
    }

    public async Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        try
        {
            // Scheduled sends must go through a Messaging Service (a From number is forbidden), and the
            // provider holds and sends the message at sendAt — this app runs no timer.
            var message = await Bounded(token => _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null,
                from: null, fallbackFrom: null, messagingServiceSid: _settings.MessagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: token), cancellationToken);

            return ToSendResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "schedule the message", cancellationToken);
        }
    }

    public async Task<bool> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await Bounded(token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: token), cancellationToken);

            // Only report success when the provider actually reads back as canceled.
            var status = updated.Status?.Value;
            return string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase);
        }
        catch (SdkException<RawError> ex)
        {
            // e.g. the message already went out and can no longer be canceled — not confirmed cancelled.
            _logger.LogWarning("Could not cancel scheduled message: provider returned HTTP {Status}.", (int)ex.Error.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "cancel the scheduled message", cancellationToken);
        }
    }

    public async Task<SmsSendResult> FetchAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(token => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: token), cancellationToken);

            return ToSendResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "read the message", cancellationToken);
        }
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        try
        {
            // Clearing the body redacts the text at the provider while preserving the record (SID,
            // status, timestamps, error code) — the fact a message was sent survives.
            await Bounded(token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: token), cancellationToken);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "dispose of the message content", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageSummary>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // Widen the provider window by a second at each edge (the exact inclusivity of the DateSent
        // comparators is not guaranteed), then filter to the exact range ourselves so the boundary is
        // deterministic.
        var lowerBound = from.AddSeconds(-1);
        var upperBound = to.AddSeconds(1);

        var collected = new List<ProviderMessageSummary>();
        int? pageArg = null;
        string? pageTokenArg = null;
        var pages = 0;
        string? nextPageUri;

        do
        {
            var response = await Bounded(token => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,          // ask the provider for OUR number's traffic only
                dateSent: null,
                dateSentQuery: upperBound,           // DateSent<  (upper bound)
                dateSentQueryQuery: lowerBound,      // DateSent>  (lower bound)
                pageSize: PageSize,
                page: pageArg,
                pageToken: pageTokenArg,
                ct: token), cancellationToken);

            if (response.Messages is not null)
            {
                foreach (var m in response.Messages)
                {
                    if (string.IsNullOrEmpty(m.Sid))
                        continue;
                    collected.Add(new ProviderMessageSummary(
                        Sid: m.Sid!,
                        Status: m.Status?.Value,
                        From: m.From,
                        To: m.To,
                        DateSent: ParseProviderDate(m.DateSent),
                        ErrorCode: m.ErrorCode));
                }
            }

            nextPageUri = response.NextPageUri;
            if (!string.IsNullOrEmpty(nextPageUri))
            {
                pageArg = int.TryParse(GetQueryValue(nextPageUri!, "Page"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
                    ? p : (int?)null;
                pageTokenArg = GetQueryValue(nextPageUri!, "PageToken");
            }

            pages++;
        }
        while (!string.IsNullOrEmpty(nextPageUri) && pages < MaxPages);

        if (pages >= MaxPages && !string.IsNullOrEmpty(nextPageUri))
            _logger.LogWarning("Reconciliation stopped at the {MaxPages}-page cap; results may be truncated.", MaxPages);

        // Deterministic exact-range filter.
        return collected
            .Where(m => m.DateSent is null || (m.DateSent >= from && m.DateSent <= to))
            .ToList();
    }

    private static SmsSendResult ToSendResult(TwilioSdk.Models.ApiV2010AccountMessage message)
    {
        var sid = message.Sid;
        var accepted = !string.IsNullOrEmpty(sid);
        return new SmsSendResult(
            Accepted: accepted,
            MessageSid: sid,
            Status: message.Status?.Value,
            ErrorCode: message.ErrorCode,
            ErrorMessage: message.ErrorMessage);
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? GetQueryValue(string uri, string key)
    {
        var q = uri.IndexOf('?');
        if (q < 0)
            return null;
        foreach (var pair in uri.Substring(q + 1).Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && string.Equals(Uri.UnescapeDataString(kv[0]), key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    /// <summary>
    /// Converts an SDK/transport failure into a caller-safe <see cref="SmsGatewayException"/>. Genuine
    /// caller cancellation is re-thrown untouched; only the provider HTTP status is logged (never the
    /// body, a secret, or a shopper's number).
    /// </summary>
    private SmsGatewayException Translate(Exception ex, string action, CancellationToken cancellationToken)
    {
        switch (ex)
        {
            case SmsGatewayException gatewayException:
                return gatewayException;

            case SdkException<RawError> sdkException:
                var status = sdkException.Error.StatusCode;
                _logger.LogWarning("Twilio error trying to {Action}: HTTP {Status}.", action, (int)status);
                return new SmsGatewayException($"Failed to {action}.", status, sdkException);

            case JsonException:
                // A 2xx body that no longer matches the model — the outcome is genuinely unknown.
                _logger.LogWarning("Twilio returned an unreadable response trying to {Action}.", action);
                return new SmsGatewayException("The messaging provider returned a response that could not be processed.", null, ex);

            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                // The caller (not our budget) cancelled — surface as cancellation, do not wrap.
                throw ex;

            default:
                // Transport failure or our own per-call budget elapsing — nothing definitive answered.
                _logger.LogWarning("Twilio was unreachable or timed out trying to {Action}.", action);
                return new SmsGatewayException("The messaging provider was unreachable or timed out.", null, ex);
        }
    }
}
