using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// The Twilio-backed <see cref="ISmsProviderGateway"/> — the only place the Twilio SDK and the auth token
/// live. Every call is bounded by a total deadline and its provider errors are translated to
/// <see cref="SmsProviderException"/>. Logs carry message SIDs and statuses only — never a destination
/// number or message body.
/// </summary>
public class TwilioSmsProviderGateway : ISmsProviderGateway
{
    // The whole-call budget the caller actually experiences (the SDK's Timeout is only per-attempt).
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProviderGateway> _logger;

    public TwilioSmsProviderGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings,
        ILogger<TwilioSmsProviderGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            // fields = null → the base validation package, returning `valid` and the canonical `phone_number`.
            LookupResponse response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: cts.Token);

            if (response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                return new PhoneValidationResult(true, response.PhoneNumber, null);
            }

            _logger.LogInformation("Twilio lookup rejected a number as not a usable destination (valid={Valid}).",
                response.Valid);
            return new PhoneValidationResult(false, null, "The provider does not consider this a usable destination.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Lookups answers 404 for a number it cannot resolve — a definitive "not usable", not an outage.
            _logger.LogInformation("Twilio lookup returned 404 for a registration attempt.");
            return new PhoneValidationResult(false, null, "The provider does not recognise this number.");
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "validate a number");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw Unreachable(ex, "validate a number", ct);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
    }

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct) =>
        CreateMessageAsync(toE164, body, scheduleType: null, sendAt: null,
            from: _settings.FromNumber, messagingServiceSid: null, "send a message", ct);

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        // Scheduling is Messaging-Service-only: use the service SID and a fixed send time, not a From number.
        CreateMessageAsync(toE164, body, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt,
            from: null, messagingServiceSid: _settings.MessagingServiceSid, "schedule a message", ct);

    private async Task<SmsSendResult> CreateMessageAsync(string toE164, string body,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, string? from, string? messagingServiceSid,
        string action, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: from, fallbackFrom: null,
                messagingServiceSid: messagingServiceSid, body: body, mediaUrl: null, contentSid: null,
                ct: cts.Token);

            var sid = msg.Sid ?? string.Empty;
            var status = msg.Status?.Value ?? string.Empty;
            _logger.LogInformation("Twilio {Action} succeeded: sid={Sid} status={Status}.", action, sid, status);
            return new SmsSendResult(sid, status, msg.ErrorCode, msg.ErrorMessage, ParseRfc2822(msg.DateSent));
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, action);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw Unreachable(ex, action, ct);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
    }

    public async Task<SmsStatusResult> FetchStatusAsync(string providerMessageSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            ApiV2010AccountMessage msg = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid, ct: cts.Token);
            return new SmsStatusResult(msg.Status?.Value ?? string.Empty, msg.ErrorCode, msg.ErrorMessage,
                ParseRfc2822(msg.DateSent));
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "fetch a message");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw Unreachable(ex, "fetch a message", ct);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled, ct: cts.Token);
            _logger.LogInformation("Twilio cancelled scheduled message sid={Sid}.", providerMessageSid);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "cancel a scheduled message");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw Unreachable(ex, "cancel a scheduled message", ct);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            // An empty body redacts the message text at the provider while the record and its status survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                body: string.Empty, status: null, ct: cts.Token);
            _logger.LogInformation("Twilio redacted content of message sid={Sid}.", providerMessageSid);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "redact a message");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw Unreachable(ex, "redact a message", ct);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
    }

    public async Task<ProviderMessagePage> ListSentFromAsync(string fromNumber, DateTimeOffset from,
        DateTimeOffset to, int? page, string? pageToken, int pageSize, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            // Ask the provider for THIS sending number's messages in the range (filtered provider-side):
            //   dateSentQueryQuery => wire DateSent> (on/after `from`); dateSentQuery => wire DateSent< (on/before `to`).
            ListMessageResponse response = await _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: fromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: pageSize,
                page: page,
                pageToken: pageToken,
                ct: cts.Token);

            var messages = new List<ProviderMessageSummary>();
            if (response.Messages is not null)
            {
                foreach (var m in response.Messages)
                {
                    messages.Add(new ProviderMessageSummary(m.Sid ?? string.Empty, m.Status?.Value, m.To, m.From,
                        ParseRfc2822(m.DateSent)));
                }
            }

            var (nextPage, nextToken) = ParseNextPage(response.NextPageUri);
            return new ProviderMessagePage(messages, nextPage, nextToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "list messages");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw Unreachable(ex, "list messages", ct);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
    }

    private SmsProviderException Translate(SdkException<RawError> ex, string action)
    {
        var status = (int)ex.Error.StatusCode;
        // ReadAsString may itself carry provider detail; it never contains our auth token or a destination number.
        _logger.LogWarning("Twilio failed to {Action}: HTTP {Status}.", action, status);
        return new SmsProviderException($"The messaging provider returned HTTP {status}.", status, ex);
    }

    private SmsProviderException Unreachable(Exception ex, string action, CancellationToken callerToken)
    {
        // A caller-initiated cancellation is distinct from our own deadline / a transport failure, but either
        // way nothing answered, so there is no status to carry.
        _logger.LogWarning("Twilio call to {Action} did not complete (transport/timeout/cancellation).", action);
        return new SmsProviderException("The messaging provider could not be reached.", null, ex);
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Extracts the Page index and PageToken from a Twilio next_page_uri (null when there is none).</summary>
    private static (int? Page, string? PageToken) ParseNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return (null, null);
        }

        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0 || queryStart == nextPageUri.Length - 1)
        {
            return (null, null);
        }

        int? page = null;
        string? token = null;
        var query = nextPageUri.Substring(queryStart + 1);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair.Substring(0, eq);
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(eq + 1));
            if (string.Equals(key, "Page", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
            {
                page = p;
            }
            else if (string.Equals(key, "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                token = value;
            }
        }

        return (page, token);
    }
}
