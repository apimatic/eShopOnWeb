using System;
using System.Collections.Generic;
using System.Net;
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

namespace Microsoft.eShopWeb.Infrastructure.Services.Notifications;

/// <summary>
/// The Twilio-backed implementation of <see cref="ISmsNotificationProvider"/>, built entirely on the
/// twilio-sdk plugin (<c>AsadAli.TwilioSdk</c>). This is the only place that talks to Twilio. It never
/// logs destination numbers, and it translates provider failures into a sanitized
/// <see cref="SmsProviderException"/> whose message carries no personal data.
/// </summary>
public class TwilioSmsNotificationProvider : ISmsNotificationProvider
{
    private const long ListPageSize = 100;
    private const int MaxListPages = 1000; // safety backstop against a non-terminating pager

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsNotificationProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber, fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null, ct: ct);

            var valid = response.Valid ?? false;
            return new PhoneValidationResult(valid, valid ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Lookup answers "not found" for a number it cannot parse — that is a validation "no",
            // not a provider outage.
            return new PhoneValidationResult(false, null);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "validate the number");
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned an unreadable response while validating the number.", ex);
        }
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        try
        {
            var message = await CreateMessageAsync(toE164, body, scheduleType: null, sendAt: null,
                from: _settings.FromNumber, messagingServiceSid: null, ct);
            return ToSendResult(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "send the message");
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned an unreadable response while sending the message.", ex);
        }
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        try
        {
            // Twilio schedules future messages only via a Messaging Service, with a fixed send time.
            var message = await CreateMessageAsync(toE164, body, scheduleType: MessageEnumScheduleType.Fixed,
                sendAt: sendAt, from: null, messagingServiceSid: _settings.MessagingServiceSid, ct);
            return ToSendResult(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "schedule the message");
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned an unreadable response while scheduling the message.", ex);
        }
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(_settings.AccountSid, providerMessageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "cancel the scheduled message");
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned an unreadable response while cancelling the scheduled message.", ex);
        }
    }

    public async Task<SmsMessageState> FetchStateAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await _client.Api20100401Message.FetchMessage(_settings.AccountSid, providerMessageSid, ct: ct);
            return new SmsMessageState(WireValue(message.Status), message.ErrorCode, message.ErrorMessage);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "read the message status");
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned an unreadable response while reading the message status.", ex);
        }
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            // An empty body on UpdateMessage redacts the stored text while the record/status survive.
            await _client.Api20100401Message.UpdateMessage(_settings.AccountSid, providerMessageSid,
                body: string.Empty, status: null, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "redact the message content");
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned an unreadable response while redacting the message content.", ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var records = new List<ProviderMessageRecord>();
        try
        {
            for (var page = 0; page < MaxListPages; page++)
            {
                // Ask the provider only for messages sent from THIS application's own number, over the
                // range — dateSentQueryQuery is the lower bound (DateSent>), dateSentQuery the upper (DateSent<).
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid, to: null, from: _settings.FromNumber,
                    dateSent: null, dateSentQuery: to, dateSentQueryQuery: from,
                    pageSize: ListPageSize, page: page, pageToken: null, ct: ct);

                if (response.Messages != null)
                {
                    foreach (var message in response.Messages)
                    {
                        records.Add(new ProviderMessageRecord(
                            message.Sid ?? string.Empty, message.To, message.From,
                            WireValue(message.Status), ParseDate(message.DateSent), message.Body));
                    }
                }

                if (response.Messages == null || response.Messages.Count == 0 || string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "list the provider's messages");
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned an unreadable response while listing messages.", ex);
        }

        return records;
    }

    private Task<ApiV2010AccountMessage> CreateMessageAsync(string toE164, string body,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, string? from, string? messagingServiceSid, CancellationToken ct)
    {
        return _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: toE164,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
            addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
            shortenUrls: null, scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null,
            contentVariables: null, riskCheck: null, from: from, fallbackFrom: null,
            messagingServiceSid: messagingServiceSid, body: body, mediaUrl: null, contentSid: null, ct: ct);
    }

    private static SmsSendResult ToSendResult(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            throw new SmsProviderException("The provider accepted the message but returned no identifier.");
        }
        return new SmsSendResult(message.Sid, WireValue(message.Status));
    }

    /// <summary>Reads the wire string from a <c>StringEnum</c> delivery status, or null when absent.</summary>
    private static string? WireValue(MessageEnumStatus? status) => status?.Value;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : (DateTimeOffset?)null;

    /// <summary>
    /// Translates an SDK error into a sanitized domain exception. Deliberately carries only the HTTP
    /// status and the numeric provider error code — never the provider's error text, which can echo the
    /// destination number back.
    /// </summary>
    private static SmsProviderException ToProviderException(SdkException<RawError> ex, string action)
    {
        int? code = null;
        try
        {
            var parsed = ex.Error.ReadAsJson<TwilioErrorBody>();
            code = parsed?.Code;
        }
        catch
        {
            // The error body did not match the expected shape; fall back to the status code alone.
        }

        var codePart = code.HasValue ? $" (provider error code {code})" : string.Empty;
        return new SmsProviderException($"The provider could not {action}: HTTP {(int)ex.Error.StatusCode}{codePart}.", ex);
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        // Intentionally not surfaced: Twilio's message text can contain the destination number.
        [JsonPropertyName("status")]
        public int? Status { get; set; }
    }
}
