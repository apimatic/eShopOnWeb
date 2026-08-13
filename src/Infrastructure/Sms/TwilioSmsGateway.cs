using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// The Twilio-backed implementation of <see cref="ISmsNotificationGateway"/>. It is the single place
/// SDK types are used: every provider/transport/unreadable-body failure is translated into
/// <see cref="SmsGatewayException"/> so the rest of the app handles one failure type, and no
/// destination number or secret is ever put into a thrown message or a log.
/// </summary>
public class TwilioSmsGateway : ISmsNotificationGateway
{
    // Total budget for a single provider call (retries + backoff must fit under it). See
    // dotnet-configuration-resilience: this is the only layer that bounds a whole call.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Backstop so a mis-paging provider can never spin forever (dotnet-configuration-resilience).
    private const int MaxReconciliationPages = 1000;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default)
    {
        try
        {
            // Lookup lives on a different host than messaging; the messaging Twilio:BaseUrl override does
            // not apply here (the SDK keeps the lookups host at its own default).
            var response = await BoundedAsync(token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null, ct: token), ct);

            bool usable = response.Valid == true
                          && (response.ValidationErrors is null || response.ValidationErrors.Count == 0)
                          && !string.IsNullOrEmpty(response.PhoneNumber);

            return usable
                ? PhoneValidationResult.Usable(response.PhoneNumber!)
                : PhoneValidationResult.NotUsable("The number is not a usable destination.");
        }
        catch (SdkException<RawError> ex)
        {
            int status = (int)ex.Error.StatusCode;
            // A hard client-side rejection (e.g. 404 not-found) is a normal "not usable" outcome, not an outage.
            if (status is >= 400 and < 500)
            {
                return PhoneValidationResult.NotUsable("The number is not a usable destination.");
            }
            throw Translate(ex, "phone lookup");
        }
        catch (JsonException ex)
        {
            // Do not turn an unreadable response into a domain "not usable" — that would fabricate a fact.
            throw new SmsGatewayException("The phone lookup returned an unreadable response.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsGatewayException("The phone lookup could not reach the provider.", ex);
        }
    }

    public Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken ct = default) =>
        CreateMessageAsync(toE164, body, from: _settings.FromNumber, messagingServiceSid: null,
            scheduleType: null, sendAt: null, senderForResult: _settings.FromNumber, action: "send", ct);

    public Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default) =>
        // Scheduling is a Messaging-Service-only capability: send through the service, not the From number.
        CreateMessageAsync(toE164, body, from: null, messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, senderForResult: _settings.MessagingServiceSid,
            action: "schedule", ct);

    private async Task<SmsDispatchResult> CreateMessageAsync(
        string to, string body, string? from, string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, string senderForResult, string action,
        CancellationToken ct)
    {
        try
        {
            var message = await BoundedAsync(token => _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: to,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: from, fallbackFrom: null,
                messagingServiceSid: messagingServiceSid, body: body, mediaUrl: null, contentSid: null,
                ct: token), ct);

            return new SmsDispatchResult(RequireSid(message), StatusOf(message), senderForResult);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, action);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException($"The provider returned an unreadable response to a {action}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsGatewayException($"The provider could not be reached to {action} a message.", ex);
        }
    }

    public async Task CancelScheduledMessageAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            await BoundedAsync(token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled, ct: token), ct);
        }
        catch (SdkException<RawError> ex) { throw Translate(ex, "cancel"); }
        catch (JsonException ex) { throw new SmsGatewayException("The provider returned an unreadable response to a cancel.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsGatewayException("The provider could not be reached to cancel a message.", ex);
        }
    }

    public async Task<SmsDeliveryState> FetchDeliveryStateAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await BoundedAsync(token => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid, ct: token), ct);
            return new SmsDeliveryState(StatusOf(message), message.ErrorCode, message.ErrorMessage);
        }
        catch (SdkException<RawError> ex) { throw Translate(ex, "status read"); }
        catch (JsonException ex) { throw new SmsGatewayException("The provider returned an unreadable status response.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsGatewayException("The provider could not be reached to read a message status.", ex);
        }
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            // Empty body redacts the content at the provider while the record survives (NOT DeleteMessage,
            // which would destroy the surviving record we must keep).
            await BoundedAsync(token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                body: string.Empty, status: null, ct: token), ct);
        }
        catch (SdkException<RawError> ex) { throw Translate(ex, "content disposal"); }
        catch (JsonException ex) { throw new SmsGatewayException("The provider returned an unreadable response to content disposal.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsGatewayException("The provider could not be reached to dispose of message content.", ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct = default)
    {
        var results = new List<ProviderMessageRecord>();
        try
        {
            for (int page = 0; page < MaxReconciliationPages; page++)
            {
                var response = await BoundedAsync(token => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,          // ask the provider to filter by our sending number
                    dateSent: null,
                    dateSentQuery: rangeEnd,             // DateSent< upper bound
                    dateSentQueryQuery: rangeStart,      // DateSent> lower bound
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: null,
                    ct: token), ct);

                var messages = response.Messages;
                if (messages is null || messages.Count == 0)
                {
                    break;
                }
                foreach (var m in messages)
                {
                    if (!string.IsNullOrEmpty(m.Sid))
                    {
                        results.Add(new ProviderMessageRecord(m.Sid!, m.Status?.Value, m.From, m.To, ParseDate(m.DateSent)));
                    }
                }
                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }
            }
        }
        catch (SdkException<RawError> ex) { throw Translate(ex, "reconciliation list"); }
        catch (JsonException ex) { throw new SmsGatewayException("The provider returned an unreadable reconciliation response.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsGatewayException("The provider could not be reached for reconciliation.", ex);
        }
        return results;
    }

    // ----- helpers -----

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static string RequireSid(ApiV2010AccountMessage message) =>
        string.IsNullOrEmpty(message.Sid)
            ? throw new SmsGatewayException("The provider accepted the message but returned no message id.")
            : message.Sid!;

    private static string StatusOf(ApiV2010AccountMessage message) => message.Status?.Value ?? "unknown";

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var dt) ? dt : null;

    private static SmsGatewayException Translate(SdkException<RawError> ex, string action)
    {
        var status = ex.Error.StatusCode;
        int? code = TryReadProviderCode(ex.Error);
        var codeText = code.HasValue ? $", provider code {code.Value}" : string.Empty;
        // Message carries only status + numeric code — never the free-text body, which can echo the number.
        return new SmsGatewayException($"Twilio {action} failed (HTTP {(int)status}{codeText}).", status, ex);
    }

    // UNVERIFIED: the SDK does not model the Twilio error body, so its shape cannot be confirmed from the
    // map/source. Read the numeric code best-effort; never let this extraction throw, and never surface the
    // free-text message (it can contain the destination number).
    private static int? TryReadProviderCode(RawError error)
    {
        try { return error.ReadAsJson<ProviderErrorBody>()?.Code; }
        catch { return null; }
    }

    private sealed class ProviderErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
