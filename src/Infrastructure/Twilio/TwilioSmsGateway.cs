using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The Twilio-backed implementation of <see cref="ISmsGateway"/>. It is the only place the Twilio SDK is
/// used. Every provider failure is translated into <see cref="SmsGatewayException"/> at this boundary, and
/// no shopper number is ever put into an exception message or log line (the provider error body, which can
/// contain the destination number, is deliberately not surfaced).
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
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
            // Lookup is served from the provider's lookups host, not the messaging host — Twilio:BaseUrl is
            // intentionally not applied to it.
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null, ct: ct);

            var isValid = response.Valid ?? false;
            return new PhoneValidationResult(isValid, isValid ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            // The provider considers the input not a usable/known number — an outcome, not a fault.
            return new PhoneValidationResult(false, null);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "validate the phone number");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        try
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null, from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null, ct: ct);

            return new SmsSendResult(message.Sid, message.Status?.Value, message.ErrorCode, message.ErrorMessage);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "send the message");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        try
        {
            // Scheduling is Messaging-Service-only: a From number cannot be used to schedule. The message is
            // held by the provider until sendAt — not by this application.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid, body: body, mediaUrl: null, contentSid: null, ct: ct);

            return new SmsSendResult(message.Sid, message.Status?.Value, message.ErrorCode, message.ErrorMessage);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "schedule the message");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
    }

    public async Task CancelScheduledAsync(string providerSid, CancellationToken ct = default)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "cancel the scheduled message");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
    }

    public async Task<SmsDeliveryState> FetchDeliveryStateAsync(string providerSid, CancellationToken ct = default)
    {
        try
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                ct: ct);

            return new SmsDeliveryState(message.Status?.Value, message.ErrorCode, message.ErrorMessage, message.DateSent);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "read the message status");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
    }

    public async Task RedactContentAsync(string providerSid, CancellationToken ct = default)
    {
        try
        {
            // An empty body redacts the message text at the provider; the record and its status survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: "",
                status: null,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "dispose of the message content");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        const int MaxPages = 50;      // backstop so a misbehaving pager can never spin forever
        const long PageSize = 1000;

        var results = new List<ProviderMessageRecord>();
        string? pageToken = null;
        int page = 0;

        try
        {
            for (int i = 0; i < MaxPages; i++)
            {
                // Ask the provider for THIS sending number's messages in the range (filter at the provider,
                // DateSent> lower bound and DateSent< upper bound), rather than filtering a wider answer.
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: PageSize,
                    page: page == 0 ? null : page,
                    pageToken: pageToken,
                    ct: ct);

                if (response.Messages is not null)
                {
                    foreach (var m in response.Messages)
                    {
                        if (string.IsNullOrEmpty(m.Sid))
                        {
                            continue;
                        }
                        results.Add(new ProviderMessageRecord(m.Sid, m.Status?.Value, ParseDate(m.DateSent), m.ErrorCode));
                    }
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }

                pageToken = ExtractQueryParam(response.NextPageUri, "PageToken");
                if (string.IsNullOrEmpty(pageToken))
                {
                    break;
                }
                page = (response.Page ?? page) + 1;
            }

            return results;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "list the provider's messages");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
    }

    private static SmsGatewayException Translate(SdkException<RawError> ex, string action)
    {
        // Carry only the HTTP status. The provider's error body can contain the destination number, so it is
        // deliberately NOT read into the message.
        var status = ex.Error.StatusCode;
        return new SmsGatewayException($"The messaging provider could not {action} (status {(int)status}).", status, ex);
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ExtractQueryParam(string uri, string name)
    {
        var query = uri.IndexOf('?');
        if (query < 0)
        {
            return null;
        }
        var pairs = uri[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            if (pair[..eq].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }
        return null;
    }
}
