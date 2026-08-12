using System;
using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// <see cref="ISmsGateway"/> backed by Twilio via the AsadAli.TwilioSdk client. This is the only type in the
/// codebase that talks to the Twilio SDK. It translates every Twilio failure into <see cref="SmsGatewayException"/>
/// and keeps phone numbers and the auth token out of every message it produces.
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

    private string AccountSid => _settings.AccountSid
        ?? throw new SmsGatewayException("Twilio:AccountSid is not configured.");

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct)
    {
        // Lookup runs on the provider's Lookups host, which the messaging BaseUrl override does not touch.
        try
        {
            var resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: ct);

            return new PhoneValidationResult(resp.Valid == true, resp.PhoneNumber);
        }
        catch (SdkException<RawError> ex)
        {
            // A number the provider cannot make sense of comes back as a 400/404 — that is a rejected
            // destination, not an outage, so surface it as "not valid" rather than an error.
            var status = (int)ex.Error.StatusCode;
            if (status is 400 or 404)
                return new PhoneValidationResult(false, null);
            throw ToGatewayException(ex, "validate a number");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("SMS provider unreachable while validating a number.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The SMS provider returned a response that could not be processed.", null, ex);
        }
    }

    public Task<SmsSubmissionResult> SendAsync(string toE164, string body, CancellationToken ct)
        => CreateMessageAsync(toE164, body, scheduleAt: null, "send a message", ct);

    public Task<SmsSubmissionResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct)
        => CreateMessageAsync(toE164, body, scheduleAt: sendAt, "schedule a message", ct);

    private async Task<SmsSubmissionResult> CreateMessageAsync(
        string toE164, string body, DateTimeOffset? scheduleAt, string action, CancellationToken ct)
    {
        // Immediate messages are sent from our configured number (so reconciliation can find them by From).
        // Scheduled messages must go through the Messaging Service, which the provider requires for scheduling.
        var isScheduled = scheduleAt.HasValue;
        try
        {
            var msg = await _client.Api20100401Message.CreateMessage(
                accountSid: AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null,
                scheduleType: isScheduled ? MessageEnumScheduleType.Fixed : null,
                sendAt: scheduleAt,
                sendAsMms: null, contentVariables: null, riskCheck: null,
                from: isScheduled ? null : _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: isScheduled ? _settings.MessagingServiceSid : null,
                body: body, mediaUrl: null, contentSid: null,
                ct: ct);

            if (string.IsNullOrEmpty(msg.Sid))
                throw new SmsGatewayException("The SMS provider accepted the message but returned no identifier.");

            return new SmsSubmissionResult(msg.Sid!, msg.Status?.Value, msg.ErrorCode, msg.ErrorMessage);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException(ex, action);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException($"SMS provider unreachable while trying to {action}.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The SMS provider returned a response that could not be processed.", null, ex);
        }
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException(ex, "cancel a scheduled message");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("SMS provider unreachable while cancelling a scheduled message.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The SMS provider returned a response that could not be processed.", null, ex);
        }
    }

    public async Task<SmsDeliveryState> FetchStatusAsync(string providerMessageSid, CancellationToken ct)
    {
        try
        {
            var msg = await _client.Api20100401Message.FetchMessage(
                accountSid: AccountSid, sid: providerMessageSid, ct: ct);
            return new SmsDeliveryState(msg.Status?.Value, msg.ErrorCode, msg.ErrorMessage);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException(ex, "fetch a message status");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("SMS provider unreachable while fetching a message status.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The SMS provider returned a response that could not be processed.", null, ex);
        }
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken ct)
    {
        // Redaction: update the message body to empty. The record and its status survive; the text does not.
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: AccountSid,
                sid: providerMessageSid,
                body: "",
                status: null,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException(ex, "dispose of message content");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("SMS provider unreachable while disposing of message content.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The SMS provider returned a response that could not be processed.", null, ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<ProviderMessageRecord>();
        string? pageToken = null;
        var safety = 0;

        try
        {
            do
            {
                var resp = await _client.Api20100401Message.ListMessage(
                    accountSid: AccountSid,
                    to: null,
                    from: _settings.FromNumber, // provider-side filter: only our sending number's messages
                    dateSent: null,
                    dateSentQuery: to,          // DateSent< upper bound
                    dateSentQueryQuery: from,   // DateSent> lower bound
                    pageSize: 200,
                    page: null,
                    pageToken: pageToken,
                    ct: ct);

                if (resp.Messages != null)
                {
                    foreach (var m in resp.Messages)
                    {
                        if (string.IsNullOrEmpty(m.Sid)) continue;
                        results.Add(new ProviderMessageRecord(
                            m.Sid!, m.From, m.To, m.Status?.Value, m.ErrorCode, TryParseDate(m.DateSent)));
                    }
                }

                pageToken = ExtractPageToken(resp.NextPageUri);
                safety++;
            }
            while (pageToken != null && safety < 100);

            return results;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException(ex, "list provider messages");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("SMS provider unreachable while listing messages.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The SMS provider returned a response that could not be processed.", null, ex);
        }
    }

    /// <summary>
    /// Translate a provider error into our own type, carrying the HTTP status so callers can tell a
    /// deterministic rejection from an outage. The message is caller-safe — no phone number, no auth token,
    /// no raw provider body (which can echo a recipient number).
    /// </summary>
    private static SmsGatewayException ToGatewayException(SdkException<RawError> ex, string action)
    {
        var status = (int)ex.Error.StatusCode;
        return new SmsGatewayException(
            $"The SMS provider rejected the request to {action} (HTTP {status}).", status, ex);
    }

    private static DateTimeOffset? TryParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>Pull the provider's opaque PageToken out of a NextPageUri so the next page can be fetched.</summary>
    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri)) return null;
        var q = nextPageUri.IndexOf('?');
        if (q < 0) return null;

        foreach (var pair in nextPageUri.Substring(q + 1).Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "PageToken" && kv[1].Length > 0)
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}
