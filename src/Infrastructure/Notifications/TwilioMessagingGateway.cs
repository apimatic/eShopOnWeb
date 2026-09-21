using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// The one place the application talks to Twilio. Every call is bounded by a request deadline and passes
/// through a single error boundary that converts provider, transport and deserialization failures into
/// <see cref="SmsGatewayException"/>. Nothing here ever logs a destination number or a message body.
/// </summary>
public class TwilioMessagingGateway : ISmsGateway
{
    // A whole-call budget (the per-attempt SDK Retry.Timeout is set separately at registration). This is
    // the only thing that bounds a call end to end.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Reconciliation page loop backstop: 50 pages x 1000 = 50k messages before we report truncation.
    private const int MaxReconciliationPages = 50;
    private const long ReconciliationPageSize = 1000;

    // Suppresses the SDK's built-in request logging for a single call — used on the Lookup, whose URL path
    // carries the shopper's number (URL paths are logged unredacted).
    private static readonly RequestOptions NoLogging = new() { LogLevel = LogLevel.None };

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingGateway> _logger;

    public TwilioMessagingGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioMessagingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidation> ValidateNumberAsync(string rawNumber, CancellationToken ct)
    {
        // Lookup is on a different host (lookups.twilio.com); the number is in the URL path, so logging is
        // suppressed for this call.
        var response = await InvokeAsync(
            "lookup",
            token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                requestOptions: NoLogging, ct: token),
            ct,
            outcomeUnknownOnTransport: false);

        var valid = response.Valid == true;
        return new PhoneNumberValidation(valid, valid ? response.PhoneNumber : null);
    }

    public async Task<ProviderMessageResult> SendAsync(string toE164, string body, CancellationToken ct)
    {
        var message = await InvokeAsync(
            "send",
            token => _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null,
                contentVariables: null, riskCheck: null,
                from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: token),
            ct,
            outcomeUnknownOnTransport: true);

        var result = ToResult(message);
        _logger.LogInformation("Sent SMS notification sid={Sid} status={State}", result.Sid, result.State);
        return result;
    }

    public async Task<ProviderMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        // Scheduling is "for Messaging Services only": messagingServiceSid + scheduleType=fixed + sendAt.
        var message = await InvokeAsync(
            "schedule",
            token => _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null,
                scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt,
                sendAsMms: null, contentVariables: null, riskCheck: null,
                from: null, fallbackFrom: null, messagingServiceSid: _settings.MessagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: token),
            ct,
            outcomeUnknownOnTransport: true);

        var result = ToResult(message);
        _logger.LogInformation("Scheduled SMS follow-up sid={Sid} status={State}", result.Sid, result.State);
        return result;
    }

    public async Task<ProviderMessageResult> FetchAsync(string providerMessageSid, CancellationToken ct)
    {
        var message = await InvokeAsync(
            "fetch",
            token => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid, ct: token),
            ct,
            outcomeUnknownOnTransport: false);

        return ToResult(message);
    }

    public async Task<ProviderMessageResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct)
    {
        var message = await InvokeAsync(
            "cancel",
            token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled, ct: token),
            ct,
            outcomeUnknownOnTransport: false);

        var result = ToResult(message);
        _logger.LogInformation("Cancelled scheduled SMS sid={Sid} status={State}", result.Sid, result.State);
        return result;
    }

    public async Task DisposeContentAsync(string providerMessageSid, CancellationToken ct)
    {
        // Posting an empty body redacts the message text at the provider (per the operation's own summary),
        // while the message record and its outcome survive.
        await InvokeAsync(
            "dispose-content",
            token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                body: string.Empty, status: null, ct: token),
            ct,
            outcomeUnknownOnTransport: false);

        _logger.LogInformation("Disposed SMS content sid={Sid}", providerMessageSid);
    }

    public async Task<ProviderMessageListing> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var all = new List<ProviderMessageSummary>();
        var truncated = false;

        for (var page = 0; ; page++)
        {
            if (page >= MaxReconciliationPages)
            {
                // Provider-independent backstop; surface the partial result rather than looping.
                truncated = true;
                _logger.LogWarning(
                    "Reconciliation listing hit the {MaxPages}-page cap; result is partial.", MaxReconciliationPages);
                break;
            }

            var pageIndex = page;
            var response = await InvokeAsync(
                "list",
                token => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,      // ask the provider for THIS number's messages only
                    dateSent: null,
                    dateSentQuery: to,               // wire DateSent<  (upper bound)
                    dateSentQueryQuery: from,         // wire DateSent>  (lower bound)
                    pageSize: ReconciliationPageSize,
                    page: pageIndex,
                    pageToken: null,
                    ct: token),
                ct,
                outcomeUnknownOnTransport: false);

            var messages = response.Messages;
            if (messages is null || messages.Count == 0)
                break;

            foreach (var m in messages)
            {
                if (string.IsNullOrEmpty(m.Sid))
                    continue;
                all.Add(new ProviderMessageSummary(m.Sid!, m.Status?.Value, m.From, ParseDate(m.DateSent)));
            }

            // No next page signalled -> done.
            if (string.IsNullOrEmpty(response.NextPageUri))
                break;
        }

        return new ProviderMessageListing(all, truncated);
    }

    // ---- boundary + mapping -------------------------------------------------------------------------

    private async Task<T> InvokeAsync<T>(
        string op, Func<CancellationToken, Task<T>> call, CancellationToken ct, bool outcomeUnknownOnTransport)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);   // the whole-call deadline — the only real bound

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            // 401/403 = our credentials/permissions; 429 = our quota — never the caller's fault.
            _logger.LogWarning("Twilio {Op} failed: HTTP {Status}.", op, status);
            throw new SmsGatewayException(
                $"The SMS provider rejected the {op} request (HTTP {status}).",
                outcomeUnknown: false, statusCode: status, inner: ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model, or an error body that did not match its shape.
            _logger.LogWarning("Twilio {Op} returned an unreadable response.", op);
            throw new SmsGatewayException(
                "The SMS provider returned a response that could not be processed.",
                outcomeUnknown: outcomeUnknownOnTransport, inner: ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // the caller cancelled (e.g. client disconnected) — not a provider fault
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Transport failure or our own deadline elapsing. For a write, the request may still have landed.
            _logger.LogWarning("Twilio {Op} could not reach the provider.", op);
            throw new SmsGatewayException(
                "The SMS provider could not be reached.",
                outcomeUnknown: outcomeUnknownOnTransport, inner: ex);
        }
    }

    private static ProviderMessageResult ToResult(ApiV2010AccountMessage m) =>
        new(m.Sid, m.Status?.Value, MapState(m.Status?.Value), m.ErrorCode, m.ErrorMessage, ParseDate(m.DateSent));

    private static NotificationDeliveryState MapState(string? rawStatus) => rawStatus switch
    {
        "delivered" or "received" or "read" or "sent" or "partially_delivered" => NotificationDeliveryState.Delivered,
        "failed" or "undelivered" => NotificationDeliveryState.Failed,
        "canceled" => NotificationDeliveryState.Cancelled,
        "queued" or "sending" or "accepted" or "scheduled" or "receiving" => NotificationDeliveryState.Pending,
        null or "" => NotificationDeliveryState.Pending,
        _ => NotificationDeliveryState.Pending
    };

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
