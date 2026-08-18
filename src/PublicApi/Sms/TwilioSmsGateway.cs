using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Sms;

/// <summary>
/// The concrete <see cref="ISmsGateway"/> over the Twilio messaging + lookup SDK. This is the sole
/// place that talks to the provider SDK.
///
/// Logging discipline: the destination number, the message body, and the auth token are NEVER
/// logged. Only provider identifiers (SIDs), statuses and HTTP status codes are.
///
/// Every call is bounded by a whole-call budget (a linked <see cref="CancellationTokenSource"/>),
/// and every failure is translated into <see cref="SmsGatewayException"/> at this boundary, so the
/// rest of the app sees exactly one failure type — and so an <em>accepted</em> message with an
/// undeliverable outcome is reported through the result, never thrown.
/// </summary>
public sealed class TwilioSmsGateway : ISmsGateway
{
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;
    private readonly TimeSpan _budget;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> options, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
        _budget = TimeSpan.FromSeconds(Math.Max(5, _settings.RequestTimeoutSeconds));
    }

    public async Task<PhoneValidationResult> ValidateDestinationAsync(string rawNumber, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        try
        {
            // Lookup resolves against a different host than messaging; the Twilio:BaseUrl override
            // deliberately does not touch it.
            LookupResponse resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null,
                countryCode: null,
                firstName: null,
                lastName: null,
                addressLine1: null,
                addressLine2: null,
                city: null,
                state: null,
                postalCode: null,
                addressCountryCode: null,
                nationalId: null,
                dateOfBirth: null,
                lastVerifiedDate: null,
                verificationSid: null,
                partnerSubId: null,
                ct: cts.Token);

            bool usable = resp.Valid == true;
            return new PhoneValidationResult(
                usable,
                resp.PhoneNumber,
                usable ? null : "The number is not a usable SMS destination.");
        }
        catch (Exception ex)
        {
            throw Translate(ex, "validate number");
        }
    }

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct) =>
        CreateMessageAsync(toE164, body, from: _settings.FromNumber, messagingServiceSid: null,
            scheduleType: null, sendAt: null, "send", ct);

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        // Scheduling is Messaging-Service-only; the provider holds and sends it at sendAt.
        CreateMessageAsync(toE164, body, from: null, messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, "schedule", ct);

    private async Task<SmsSendResult> CreateMessageAsync(
        string toE164, string body, string? from, string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, string operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        try
        {
            ApiV2010AccountMessage msg;
            // Hold the create to a single network send: a transport-level retry of a paid SMS is refused.
            using (SingleSendGuardHandler.BeginSingleSend())
            {
                msg = await _client.Api20100401Message.CreateMessage(
                    accountSid: _settings.AccountSid,
                    to: toE164,
                    statusCallback: null,
                    applicationSid: null,
                    maxPrice: null,
                    provideFeedback: null,
                    attempt: null,
                    validityPeriod: null,
                    forceDelivery: null,
                    contentRetention: null,
                    addressRetention: null,
                    smartEncoded: null,
                    persistentAction: null,
                    trafficType: null,
                    shortenUrls: null,
                    scheduleType: scheduleType,
                    sendAt: sendAt,
                    sendAsMms: null,
                    contentVariables: null,
                    riskCheck: null,
                    from: from,
                    fallbackFrom: null,
                    messagingServiceSid: messagingServiceSid,
                    body: body,
                    mediaUrl: null,
                    contentSid: null,
                    ct: cts.Token);
            }

            var result = ToResult(msg);
            _logger.LogInformation("Twilio {Operation} accepted: sid={Sid} status={Status}", operation, result.ProviderMessageSid ?? "(none)", result.Status);
            return result;
        }
        catch (Exception ex)
        {
            throw Translate(ex, operation);
        }
    }

    public async Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        try
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: cts.Token);

            _logger.LogInformation("Twilio cancel scheduled: sid={Sid} status={Status}", providerMessageSid, msg.Status?.Value ?? DeliveryStatuses.Unknown);
            return ToResult(msg);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "cancel scheduled");
        }
    }

    public async Task<SmsSendResult> FetchAsync(string providerMessageSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        try
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: cts.Token);

            return ToResult(msg);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "fetch message");
        }
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        try
        {
            // Empty body redacts the text at the provider while the record + status survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: cts.Token);

            _logger.LogInformation("Twilio content redacted: sid={Sid}", providerMessageSid);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "redact content");
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);

        var records = new List<ProviderMessageRecord>();
        try
        {
            int page = 0;
            while (true)
            {
                // Ask the provider only for THIS application's sending number, over the DateSent range.
                // dateSentQueryQuery = DateSent> (start), dateSentQuery = DateSent< (end).
                ListMessageResponse resp = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: null,
                    ct: cts.Token);

                var messages = resp.Messages;
                if (messages is null || messages.Count == 0)
                {
                    break;
                }

                foreach (var msg in messages)
                {
                    if (msg.Sid is null)
                    {
                        continue;
                    }
                    records.Add(new ProviderMessageRecord(
                        msg.Sid,
                        msg.Status?.Value ?? DeliveryStatuses.Unknown,
                        ParseDate(msg.DateSent),
                        msg.ErrorCode,
                        msg.ErrorMessage));
                }

                // Stop conditions that do NOT depend solely on provider cooperation: a short page is the
                // last page, an absent next-page link ends the walk, and a hard page cap is the backstop.
                if (messages.Count < ReconciliationPageSize || string.IsNullOrEmpty(resp.NextPageUri))
                {
                    break;
                }

                page++;
                if (page >= MaxReconciliationPages)
                {
                    _logger.LogWarning("Reconciliation stopped at page cap {Cap}; range may exceed {PerPage}x{Cap} messages.", MaxReconciliationPages, ReconciliationPageSize);
                    break;
                }
            }

            return records;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "list messages");
        }
    }

    private static SmsSendResult ToResult(ApiV2010AccountMessage msg) =>
        new(msg.Sid,
            msg.Status?.Value ?? DeliveryStatuses.Unknown,
            msg.ErrorCode,
            msg.ErrorMessage,
            ParseDate(msg.DateSent));

    // ApiV2010AccountMessage.DateSent is the provider's date string (RFC-2822); parse it defensively.
    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>
    /// Translate every failure kind into <see cref="SmsGatewayException"/>, carrying the provider's
    /// HTTP status where one exists. Note the two opposite <see cref="JsonException"/> cases: a broken
    /// 2xx body is a genuinely unknown outcome (no status), whereas a non-2xx whose body did not match
    /// its generated error shape is still a rejection whose status was lost — both surface here as a
    /// gateway failure with a caller-safe message, never the raw provider text (which could echo the
    /// destination number).
    /// </summary>
    private SmsGatewayException Translate(Exception ex, string operation)
    {
        switch (ex)
        {
            case SdkException<RawError> sdk:
                int status = (int)sdk.Error.StatusCode;
                _logger.LogWarning("Twilio {Operation} failed: httpStatus={Status}", operation, status);
                return new SmsGatewayException($"The messaging provider rejected the {operation} request.", status, sdk);

            case DuplicateSendRefusedException dup:
                _logger.LogWarning("Twilio {Operation}: duplicate send refused after transport failure; outcome unknown.", operation);
                return new SmsGatewayException("The messaging provider send outcome is unknown after a transport failure.", null, dup);

            case OperationCanceledException:
            case HttpRequestException:
                _logger.LogWarning("Twilio {Operation} failed: provider unreachable or timed out.", operation);
                return new SmsGatewayException("The messaging provider was unreachable.", null, ex);

            case JsonException:
                _logger.LogWarning("Twilio {Operation} failed: provider returned an unprocessable response.", operation);
                return new SmsGatewayException("The messaging provider returned a response that could not be processed.", null, ex);

            default:
                _logger.LogWarning("Twilio {Operation} failed: unexpected error {Type}.", operation, ex.GetType().Name);
                return new SmsGatewayException("An unexpected error occurred talking to the messaging provider.", null, ex);
        }
    }
}
