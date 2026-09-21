using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// Twilio implementation of <see cref="ISmsProvider"/>. Every Twilio interaction goes through the vendored
/// APIMatic-generated SDK client. All SDK types stay inside this adapter; the domain sees only the plain DTOs.
/// The shopper's number and message body are never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    // Bounds a single logical provider call (the request's own token still applies on top of this).
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);
    private const int MaxReconciliationPages = 50;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidation> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken)
    {
        using var scope = Bounded(cancellationToken, out var ct);
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: ct);

            var valid = response.Valid ?? false;
            if (valid && !string.IsNullOrEmpty(response.PhoneNumber))
            {
                return new PhoneNumberValidation(true, response.PhoneNumber, null);
            }

            var reason = response.ValidationErrors is { Count: > 0 }
                ? "the provider reported validation errors"
                : "the provider does not consider it a valid destination";
            return new PhoneNumberValidation(false, null, reason);
        }
        catch (SdkException<RawError> ex) when (IsClientNumberError(ex.Error.StatusCode))
        {
            // A 400/404 from Lookups is a statement about the number, not a provider outage: treat as invalid.
            return new PhoneNumberValidation(false, null, "the provider could not resolve the number");
        }
        catch (Exception ex)
        {
            throw Translate(ex, "validate a phone number", cancellationToken);
        }
    }

    public async Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken cancellationToken)
    {
        using var scope = Bounded(cancellationToken, out var ct);
        try
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null,
                contentRetention: null, addressRetention: null, smartEncoded: null,
                persistentAction: null, trafficType: null, shortenUrls: null,
                scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null,
                from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: ct);

            return DescribeAccepted(message);
        }
        catch (Exception ex)
        {
            return DescribeSendFailure(ex, "send a message", cancellationToken);
        }
    }

    public async Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        using var scope = Bounded(cancellationToken, out var ct);
        try
        {
            // Scheduled sends go through the Messaging Service (no explicit From) with a fixed send time.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null,
                contentRetention: null, addressRetention: null, smartEncoded: null,
                persistentAction: null, trafficType: null, shortenUrls: null,
                scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null, contentVariables: null,
                riskCheck: null,
                from: null, fallbackFrom: null, messagingServiceSid: _settings.MessagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: ct);

            return DescribeAccepted(message);
        }
        catch (Exception ex)
        {
            return DescribeSendFailure(ex, "schedule a message", cancellationToken);
        }
    }

    public async Task<SmsDispatchResult> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        using var scope = Bounded(cancellationToken, out var ct);
        try
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);

            return SmsDispatchResult.Ok(message.Sid ?? providerMessageSid, message.Status?.Value);
        }
        catch (Exception ex)
        {
            return DescribeSendFailure(ex, "cancel a scheduled message", cancellationToken);
        }
    }

    public async Task<SmsDeliveryStatus> FetchStatusAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        using var scope = Bounded(cancellationToken, out var ct);
        try
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: ct);

            return new SmsDeliveryStatus(message.Status?.Value, message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "fetch a message status", cancellationToken);
        }
    }

    public async Task DeleteContentAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        using var scope = Bounded(cancellationToken, out var ct);
        try
        {
            await _client.Api20100401Message.DeleteMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: ct);
            _logger.LogInformation("Disposed message content at provider (sid {Sid}).", providerMessageSid);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "delete a message", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // One bounded budget for the whole page loop.
        using var scope = Bounded(cancellationToken, out var ct);
        var results = new List<ProviderMessageRecord>();
        string? pageToken = null;
        var pages = 0;
        try
        {
            do
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,          // ask the provider only for this app's sending number
                    dateSent: null,
                    dateSentQuery: to,                   // wire DateSent<  (upper bound)
                    dateSentQueryQuery: from,            // wire DateSent>  (lower bound)
                    pageSize: ReconciliationPageSize,
                    page: null,
                    pageToken: pageToken,
                    ct: ct);

                if (response.Messages is { Count: > 0 })
                {
                    results.AddRange(response.Messages
                        .Where(m => m.Sid is not null)
                        .Select(m => new ProviderMessageRecord(
                            m.Sid!, m.Status?.Value, m.To, m.From, ParseDate(m.DateSent), m.ErrorCode)));
                }

                pageToken = ExtractPageToken(response.NextPageUri);
                pages++;
            }
            while (pageToken is not null && pages < MaxReconciliationPages);

            if (pageToken is not null && pages >= MaxReconciliationPages)
            {
                _logger.LogWarning("Reconciliation stopped at the {MaxPages}-page cap; results may be incomplete.", MaxReconciliationPages);
            }

            return results;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "list messages for reconciliation", cancellationToken);
        }
    }

    // --- helpers ---

    private static SmsDispatchResult DescribeAccepted(TwilioSdk.Models.ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
            return SmsDispatchResult.Failed("the provider did not return a message identifier");
        return new SmsDispatchResult(message.Sid, message.Status?.Value, message.ErrorCode, message.ErrorMessage, null);
    }

    private SmsDispatchResult DescribeSendFailure(Exception ex, string action, CancellationToken originalToken)
    {
        // A genuine caller cancellation propagates; everything else becomes a non-fatal send failure so it
        // never fails the underlying order operation.
        if (originalToken.IsCancellationRequested)
            throw new OperationCanceledException(originalToken);

        var translated = Translate(ex, action, originalToken);
        _logger.LogWarning("Could not {Action}: {Reason}", action, translated.Message);
        return SmsDispatchResult.Failed(translated.Message);
    }

    private static SmsProviderException Translate(Exception ex, string action, CancellationToken originalToken)
    {
        switch (ex)
        {
            case SmsProviderException spe:
                return spe;
            case SdkException<RawError> sdk:
                // Case B: RawError carries the status.
                return new SmsProviderException($"Provider rejected the request to {action}.", sdk.Error.StatusCode, sdk);
            case System.Text.Json.JsonException:
                return new SmsProviderException($"The provider returned a response that could not be processed while trying to {action}.", null, ex);
            case OperationCanceledException when originalToken.IsCancellationRequested:
                throw new OperationCanceledException(originalToken);
            case OperationCanceledException:
                // Our own per-call timeout elapsed.
                return new SmsProviderException($"The provider did not respond in time while trying to {action}.", null, ex);
            case HttpRequestException:
                return new SmsProviderException($"The provider was unreachable while trying to {action}.", null, ex);
            default:
                return new SmsProviderException($"Unexpected error while trying to {action}.", null, ex);
        }
    }

    private static bool IsClientNumberError(HttpStatusCode status) =>
        status == HttpStatusCode.BadRequest || status == HttpStatusCode.NotFound;

    private IDisposable Bounded(CancellationToken outer, out CancellationToken bounded)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(CallTimeout);
        bounded = cts.Token;
        return cts;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : (DateTimeOffset?)null;

    /// <summary>Extract the <c>PageToken</c> query value from a Twilio next-page URI, if present.</summary>
    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
            return null;

        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0 || queryStart == nextPageUri.Length - 1)
            return null;

        var query = nextPageUri.Substring(queryStart + 1);
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair.Substring(0, eq);
            if (string.Equals(key, "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                var value = pair.Substring(eq + 1);
                return string.IsNullOrEmpty(value) ? null : Uri.UnescapeDataString(value);
            }
        }
        return null;
    }
}
