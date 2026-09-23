using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The one place that talks to Twilio. Every SDK call is bounded by a total deadline (the SDK's own Timeout is
/// per-attempt) and every provider/transport failure is translated to <see cref="SmsGatewayException"/> so the
/// rest of the app has a single failure type. Message operations are Case B (<c>SdkException&lt;RawError&gt;</c>);
/// a drifted 2xx body or an error body that doesn't match its shape surfaces as <see cref="JsonException"/>, so
/// both are caught here. Recipient numbers and message bodies are never logged.
/// </summary>
public sealed class TwilioSmsGateway : ISmsGateway
{
    // Whole-call budget. The SDK's per-attempt Timeout does not bound a call; a CancellationToken deadline does.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly TwilioSdkClient _client;
    private readonly IAppLogger<TwilioSmsGateway> _logger;
    private readonly string _accountSid;
    private readonly string _fromNumber;
    private readonly string _messagingServiceSid;
    private readonly int _followUpDelayDays;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioOptions> options, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _logger = logger;
        var o = options.Value;
        _accountSid = o.AccountSid;
        _fromNumber = o.FromNumber;
        _messagingServiceSid = o.MessagingServiceSid;
        _followUpDelayDays = o.FollowUpDelayDays;
    }

    public string FromNumber => _fromNumber;

    public Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct) =>
        Bounded(async token =>
        {
            try
            {
                // Lookup lives on a different host (Default4); not governed by Twilio:BaseUrl.
                LookupResponse resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: null, countryCode: null, firstName: null, lastName: null,
                    addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                    addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                    verificationSid: null, partnerSubId: null,
                    ct: token);

                return new PhoneValidationResult(resp.Valid == true, resp.PhoneNumber);
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
            {
                // The provider could not parse the number at all — not a usable destination.
                return new PhoneValidationResult(false, null);
            }
        }, ct);

    public Task<SentMessage> SendAsync(string toNumber, string body, CancellationToken ct) =>
        Bounded(async token =>
        {
            ApiV2010AccountMessage resp = await _client.Api20100401Message.CreateMessage(
                accountSid: _accountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null, from: _fromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: token);

            return ToSentMessage(resp);
        }, ct);

    public Task<SentMessage> ScheduleFollowUpAsync(string toNumber, string body, CancellationToken ct) =>
        Bounded(async token =>
        {
            var sendAt = DateTimeOffset.UtcNow.AddDays(_followUpDelayDays);
            // Provider-side scheduling is Messaging-Service-only: from must be null, scheduleType=fixed, sendAt set.
            ApiV2010AccountMessage resp = await _client.Api20100401Message.CreateMessage(
                accountSid: _accountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
                messagingServiceSid: _messagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: token);

            return ToSentMessage(resp);
        }, ct);

    public Task CancelScheduledAsync(string messageSid, CancellationToken ct) =>
        Bounded<object?>(async token =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _accountSid, sid: messageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled,
                ct: token);
            return null;
        }, ct);

    public Task<MessageState> FetchStateAsync(string messageSid, CancellationToken ct) =>
        Bounded(async token =>
        {
            ApiV2010AccountMessage resp = await _client.Api20100401Message.FetchMessage(
                accountSid: _accountSid, sid: messageSid, ct: token);

            return new MessageState(resp.Status?.Value, resp.ErrorCode, resp.ErrorMessage, ParseDate(resp.DateSent));
        }, ct);

    public Task RedactBodyAsync(string messageSid, CancellationToken ct) =>
        Bounded<object?>(async token =>
        {
            // Redaction: replacing the body with an empty string removes the text at the provider while the
            // record (status, dates) survives. This is not DeleteMessage, which would destroy the whole record.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _accountSid, sid: messageSid,
                body: string.Empty, status: null,
                ct: token);
            return null;
        }, ct);

    public Task<ProviderMessageList> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Bounded(async token =>
        {
            const int MaxPages = 50;         // backstop so the page walk cannot depend only on the provider
            const long PageSize = 100;
            var messages = new List<ProviderMessage>();
            int pages = 0;
            int? page = null;
            string? pageToken = null;
            bool truncated = false;

            while (true)
            {
                // Ask the provider only for this application's sending number, within the date-sent window.
                // DateSent> (dateSentQueryQuery) = window start; DateSent< (dateSentQuery) = window end.
                ListMessageResponse resp = await _client.Api20100401Message.ListMessage(
                    accountSid: _accountSid,
                    to: null,
                    from: _fromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: PageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: token);

                if (resp.Messages is not null)
                {
                    foreach (var m in resp.Messages)
                    {
                        messages.Add(new ProviderMessage(
                            m.Sid, m.Status?.Value, m.From, m.To, ParseDate(m.DateSent), m.ErrorCode));
                    }
                }

                pages++;
                if (string.IsNullOrEmpty(resp.NextPageUri))
                    break;

                if (pages >= MaxPages)
                {
                    truncated = true;
                    break;
                }

                (page, pageToken) = ParseNextPage(resp.NextPageUri!);
                if (page is null && pageToken is null)
                    break; // cannot advance — stop rather than loop forever
            }

            return new ProviderMessageList(messages, truncated, pages);
        }, ct);

    private static SentMessage ToSentMessage(ApiV2010AccountMessage resp) =>
        new(resp.Sid, resp.Status?.Value, resp.ErrorCode, resp.ErrorMessage, ParseDate(resp.DateSent));

    private static DateTimeOffset? ParseDate(string? rfc2822) =>
        DateTimeOffset.TryParse(rfc2822, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;

    // Twilio's next_page_uri is a relative URI carrying Page and PageToken query params.
    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        int? page = null;
        string? token = null;
        var qIndex = nextPageUri.IndexOf('?');
        if (qIndex < 0 || qIndex == nextPageUri.Length - 1) return (null, null);

        foreach (var pair in nextPageUri.Substring(qIndex + 1).Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var key = pair.Substring(0, eq);
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
            if (string.Equals(key, "Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
                page = p;
            else if (string.Equals(key, "PageToken", StringComparison.OrdinalIgnoreCase))
                token = value;
        }
        return (page, token);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            // Log status only — never the response body (it can echo the recipient number).
            _logger.LogWarning($"Twilio API returned HTTP {status}.");
            throw new SmsGatewayException($"Provider returned HTTP {status}.", status, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Twilio returned a response that could not be processed.");
            throw new SmsGatewayException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — propagate
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning("Twilio call exceeded its time budget.");
            throw new SmsGatewayException("The provider call timed out.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Twilio provider was unreachable.");
            throw new SmsGatewayException("The provider is unreachable.", null, ex);
        }
    }
}
