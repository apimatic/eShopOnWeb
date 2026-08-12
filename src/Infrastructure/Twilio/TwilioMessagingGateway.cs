using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The single seam over the Twilio messaging + lookup APIs. Translates every provider failure into
/// <see cref="NotificationProviderException"/>, bounds each call with a total budget, and never logs
/// the destination number or the auth token.
/// </summary>
public class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxReconciliationPages = 200;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingGateway> _logger;

    public TwilioMessagingGateway(TwilioSdkClient client, TwilioSettings settings, IAppLogger<TwilioMessagingGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PhoneNumberValidation> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        using var cts = LinkedBudget(ct);
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: cts.Token);

            var isValid = response.Valid ?? false;
            var reasons = response.ValidationErrors?.Select(v => v.Value).ToList() ?? new List<string>();
            return new PhoneNumberValidation(isValid, response.PhoneNumber, reasons);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "phone-number validation");
        }
    }

    public async Task<ProviderSendResult> SendMessageAsync(string toNumber, string body, CancellationToken ct = default)
    {
        using var cts = LinkedBudget(ct);
        try
        {
            var useService = !string.IsNullOrWhiteSpace(_settings.MessagingServiceSid);
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null,
                from: useService ? null : _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: useService ? _settings.MessagingServiceSid : null,
                body: body,
                mediaUrl: null, contentSid: null,
                ct: cts.Token);

            return ToSendResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "send");
        }
    }

    public async Task<ProviderSendResult> ScheduleMessageAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new NotificationProviderException("Scheduling a message requires a configured messaging service.");
        }

        using var cts = LinkedBudget(ct);
        try
        {
            // Scheduling is a Messaging-Service-only feature: pass the service and no 'from'.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null,
                scheduleType: MessageEnumScheduleType.Fixed,
                sendAt: sendAt,
                sendAsMms: null, contentVariables: null, riskCheck: null,
                from: null, fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body,
                mediaUrl: null, contentSid: null,
                ct: cts.Token);

            return ToSendResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "schedule");
        }
    }

    public async Task<ProviderMessageState> CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default)
    {
        using var cts = LinkedBudget(ct);
        try
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: cts.Token);

            return ToState(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "cancel scheduled message");
        }
    }

    public async Task<ProviderMessageState> FetchMessageStateAsync(string messageSid, CancellationToken ct = default)
    {
        using var cts = LinkedBudget(ct);
        try
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                ct: cts.Token);

            return ToState(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "fetch message");
        }
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default)
    {
        using var cts = LinkedBudget(ct);
        try
        {
            // An empty-string body replaces (redacts) the stored text at the provider; the record survives.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: string.Empty,
                status: null,
                ct: cts.Token);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "redact message");
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var records = new List<ProviderMessageRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var cts = LinkedBudget(ct);

        var page = 0;
        while (true)
        {
            ListMessageResponse response;
            try
            {
                // Filter by sender in the provider request (From = our configured number), and by the
                // DateSent range: dateSentQueryQuery is the lower bound (DateSent>), dateSentQuery the upper (DateSent<).
                response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: 100L,
                    page: page,
                    pageToken: null,
                    ct: cts.Token);
            }
            catch (Exception ex)
            {
                throw Translate(ex, "reconciliation list");
            }

            var messages = response.Messages ?? new List<ApiV2010AccountMessage>();
            foreach (var message in messages)
            {
                if (string.IsNullOrEmpty(message.Sid) || !seen.Add(message.Sid))
                {
                    continue;
                }

                records.Add(new ProviderMessageRecord(
                    message.Sid,
                    message.Status?.Value ?? "unknown",
                    message.DateSent,
                    message.ErrorCode));
            }

            if (messages.Count == 0 || string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }

            page++;
            if (page >= MaxReconciliationPages)
            {
                _logger.LogWarning("Reconciliation reached the page cap of {Cap}; the report may be truncated.", MaxReconciliationPages);
                break;
            }
        }

        return records;
    }

    // ----- helpers -----

    private static ProviderSendResult ToSendResult(ApiV2010AccountMessage message) =>
        new(message.Sid,
            message.Status?.Value ?? "unknown",
            message.ErrorCode,
            message.ErrorMessage);

    private static ProviderMessageState ToState(ApiV2010AccountMessage message) =>
        new(message.Status?.Value ?? "unknown",
            message.ErrorCode,
            message.ErrorMessage);

    private static CancellationTokenSource LinkedBudget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    /// <summary>
    /// Translate any provider failure into a caller-safe <see cref="NotificationProviderException"/>.
    /// The provider's HTTP status is carried; the raw body and the destination number are not.
    /// </summary>
    private NotificationProviderException Translate(Exception ex, string action)
    {
        switch (ex)
        {
            case SdkException<RawError> sdk:
                var status = sdk.Error.StatusCode;
                var providerCode = TryReadProviderCode(sdk.Error);
                _logger.LogWarning("Twilio {Action} failed: HTTP {Status}, provider code {Code}.",
                    action, (int)status, providerCode?.ToString() ?? "n/a");
                return new NotificationProviderException(
                    $"The messaging provider rejected the {action} request.", status, ex);

            case JsonException:
                _logger.LogWarning("Twilio {Action} returned a response that could not be processed.", action);
                return new NotificationProviderException(
                    $"The messaging provider returned a response that could not be processed ({action}).", null, ex);

            case OperationCanceledException:
            case HttpRequestException:
                _logger.LogWarning("Twilio {Action} could not reach the messaging provider.", action);
                return new NotificationProviderException(
                    $"The messaging provider was unreachable ({action}).", null, ex);

            default:
                _logger.LogWarning("Twilio {Action} failed unexpectedly.", action);
                return new NotificationProviderException(
                    $"An unexpected error occurred contacting the messaging provider ({action}).", null, ex);
        }
    }

    /// <summary>Best-effort extraction of Twilio's numeric error code from the error body, for logging only.</summary>
    private static int? TryReadProviderCode(RawError error)
    {
        try
        {
            var dto = error.ReadAsJson<ProviderErrorBody>();
            return dto?.Code;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class ProviderErrorBody
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
