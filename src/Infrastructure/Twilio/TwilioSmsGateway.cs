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
using Twilio;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio-backed <see cref="ISmsGateway"/>. Every provider call is bounded by a per-call deadline and every
/// failure — a provider error response, a malformed body, a transport failure, a timeout — is translated
/// into a single <see cref="SmsGatewayException"/> carrying a caller-safe message (no phone number, no
/// provider internals) and the HTTP status when one was available. The SDK's own logging is disabled at
/// registration, so a phone number can never leak through it (the lookup number rides in the URL path).
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    // A per-call deadline. Sends are POST and are not resent by the SDK, so this bounds one attempt.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private const int MaxReconciliationPages = 200;
    private const long PageSize = 1000;

    private readonly TwilioClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            try
            {
                var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber,
                    fields: null, countryCode: null, firstName: null, lastName: null,
                    addressLine1: null, addressLine2: null, city: null, state: null,
                    postalCode: null, addressCountryCode: null, nationalId: null,
                    dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                    partnerSubId: null, ct: token);
                return new PhoneValidationResult(response.Valid == true, response.PhoneNumber);
            }
            catch (SdkException<RawError> ex) when (
                ex.Error.StatusCode == HttpStatusCode.NotFound || ex.Error.StatusCode == HttpStatusCode.BadRequest)
            {
                // The provider could not treat the input as a usable number — that is an invalid destination,
                // not an outage. (Auth/rate-limit/5xx fall through to ExecuteAsync's boundary and throw.)
                return new PhoneValidationResult(false, null);
            }
        }, "validate the phone number", ct);

    public Task<SmsDispatchResult> SendAsync(string to, string body, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            var message = await CreateMessageAsync(to, body, scheduleType: null, sendAt: null, useMessagingService: false, token);
            return ToDispatchResult(message);
        }, "send the message", ct);

    public Task<SmsDispatchResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            // Scheduling requires a Messaging Service and scheduleType=fixed; the provider holds the message.
            var message = await CreateMessageAsync(to, body, MessageEnumScheduleType.Fixed, sendAt, useMessagingService: true, token);
            return ToDispatchResult(message);
        }, "schedule the follow-up message", ct);

    public Task CancelScheduledAsync(string messageSid, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            await _client.Api20100401Message.UpdateMessage(
                _settings.AccountSid, messageSid, body: null, status: MessageEnumUpdateStatus.Canceled, ct: token);
            return true;
        }, "cancel the scheduled message", ct);

    public Task<SmsDeliveryState> FetchStateAsync(string messageSid, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            var message = await _client.Api20100401Message.FetchMessage(_settings.AccountSid, messageSid, ct: token);
            return new SmsDeliveryState(message.Status?.Value, message.ErrorCode, message.ErrorMessage);
        }, "fetch the message status", ct);

    public Task DisposeContentAsync(string messageSid, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            // Updating the body to empty redacts the message text at the provider while keeping the record.
            await _client.Api20100401Message.UpdateMessage(
                _settings.AccountSid, messageSid, body: string.Empty, status: null, ct: token);
            return true;
        }, "dispose of the message content", ct);

    public Task<IReadOnlyList<ProviderMessage>> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            var messages = new List<ProviderMessage>();
            int? page = null;
            string? pageToken = null;
            var pages = 0;

            do
            {
                // Ask the provider only for messages sent from THIS application's own number, over the range.
                // dateSentQuery maps to `DateSent<` (upper bound = to); dateSentQueryQuery to `DateSent>` (lower = from).
                var response = await _client.Api20100401Message.ListMessage(
                    _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: PageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: token);

                if (response.Messages is not null)
                {
                    foreach (var m in response.Messages)
                    {
                        if (m.Sid is not null)
                        {
                            messages.Add(new ProviderMessage(m.Sid, m.Status?.Value, m.To, m.From, m.DateSent));
                        }
                    }
                }

                (page, pageToken) = ParseNextPage(response.NextPageUri);
            }
            while (pageToken is not null && ++pages < MaxReconciliationPages);

            return (IReadOnlyList<ProviderMessage>)messages;
        }, "list the provider's messages", ct);

    // --- helpers -------------------------------------------------------------------------------------

    private Task<ApiV2010AccountMessage> CreateMessageAsync(
        string to, string body, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool useMessagingService, CancellationToken token) =>
        _client.Api20100401Message.CreateMessage(
            _settings.AccountSid,
            to,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null, attempt: null,
            validityPeriod: null, forceDelivery: null, contentRetention: null, addressRetention: null,
            smartEncoded: null, persistentAction: null, trafficType: null, shortenUrls: null,
            scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null, contentVariables: null, riskCheck: null,
            from: useMessagingService ? null : _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: useMessagingService ? _settings.MessagingServiceSid : null,
            body: body,
            mediaUrl: null, contentSid: null, ct: token);

    private static SmsDispatchResult ToDispatchResult(ApiV2010AccountMessage message) =>
        new(message.Sid, message.Status?.Value, message.ErrorCode, message.ErrorMessage);

    /// <summary>Extract the Page and PageToken query values from a provider next-page URI, if present.</summary>
    private static (int? Page, string? PageToken) ParseNextPage(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return (null, null);
        }

        var queryStart = nextPageUri!.IndexOf('?');
        if (queryStart < 0)
        {
            return (null, null);
        }

        int? page = null;
        string? pageToken = null;
        foreach (var pair in nextPageUri.Substring(queryStart + 1).Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }
            var key = Uri.UnescapeDataString(pair.Substring(0, eq));
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
            if (key == "Page" && int.TryParse(value, out var p))
            {
                page = p;
            }
            else if (key == "PageToken")
            {
                pageToken = value;
            }
        }

        return (page, pageToken);
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, string action, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await operation(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            // Case B: the error IS a RawError carrying the status. Never surface its body (may echo the number).
            throw new SmsGatewayException(
                $"The SMS provider could not {action} (HTTP {(int)ex.Error.StatusCode}).", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model, or an error body that didn't match its shape.
            throw new SmsGatewayException(
                $"The SMS provider returned a response that could not be processed while trying to {action}.", null, ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our own per-call budget elapsed (the caller did not cancel).
            throw new SmsGatewayException($"The SMS provider timed out while trying to {action}.", HttpStatusCode.GatewayTimeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException($"The SMS provider was unreachable while trying to {action}.", null, ex);
        }
    }
}
