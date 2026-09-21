using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio-backed <see cref="ISmsGateway"/>. The only place that talks to the Twilio SDK. Every call
/// is bounded by a total budget (the SDK's own Timeout is per-attempt), and provider/transport faults
/// are translated into <see cref="SmsGatewayException"/> or, for the send paths, into a failed
/// <see cref="SmsSendResult"/>. Phone numbers and message bodies are never logged.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    // Synthetic status recorded when the create call itself was rejected (no provider SID exists).
    private const string NotSentStatus = "not_sent";

    // Whole-call budget — the SDK's Timeout bounds one attempt, only a token bounds the whole call.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Backstop so a misbehaving pager can never spin forever (1000 pages × 1000 = 1M messages).
    private const int MaxPages = 1000;
    private const long PageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioOptions> options, ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string ConfiguredSendingNumber => _options.FromNumber;

    public async Task<PhoneNumberValidationResult> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken ct)
    {
        try
        {
            return await Bounded(async token =>
            {
                var resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: null, countryCode: null, firstName: null, lastName: null,
                    addressLine1: null, addressLine2: null, city: null, state: null,
                    postalCode: null, addressCountryCode: null, nationalId: null,
                    dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                    partnerSubId: null, ct: token);

                var valid = resp.Valid == true;
                return new PhoneNumberValidationResult(
                    valid,
                    valid ? resp.PhoneNumber : null,
                    valid ? null : "The number is not a usable destination.");
            }, ct);
        }
        catch (SdkException<RawError> ex) when (
            ex.Error.StatusCode == HttpStatusCode.NotFound || ex.Error.StatusCode == HttpStatusCode.BadRequest)
        {
            // The provider does not recognise this as a usable number — reject at registration.
            return new PhoneNumberValidationResult(false, null, "The number is not a usable destination.");
        }
        catch (Exception ex)
        {
            throw Translate("validate phone number", ex);
        }
    }

    public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct) =>
        CreateMessageAsync(to, body, scheduled: false, sendAt: null, ct);

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        CreateMessageAsync(to, body, scheduled: true, sendAt: sendAt, ct);

    private async Task<SmsSendResult> CreateMessageAsync(
        string to, string body, bool scheduled, DateTimeOffset? sendAt, CancellationToken ct)
    {
        try
        {
            return await Bounded(async token =>
            {
                var resp = await _client.Api20100401Message.CreateMessage(
                    accountSid: _options.AccountSid,
                    to: to,
                    statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                    attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                    addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                    shortenUrls: null,
                    // Scheduling is Messaging-Service-only: ScheduleType=fixed + SendAt + MessagingServiceSid.
                    scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
                    sendAt: scheduled ? sendAt : null,
                    sendAsMms: null, contentVariables: null, riskCheck: null,
                    // Immediate sends go from the configured number; scheduled sends omit From and
                    // use the Messaging Service instead.
                    from: scheduled ? null : _options.FromNumber,
                    fallbackFrom: null,
                    messagingServiceSid: scheduled ? _options.MessagingServiceSid : null,
                    body: body, mediaUrl: null, contentSid: null,
                    ct: token);

                return new SmsSendResult(resp.Sid, resp.Status?.Value, resp.ErrorCode, resp.ErrorMessage);
            }, ct);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation — record it as
            // a failed send outcome instead of throwing. No PII (number/body) in the log.
            _logger.LogWarning("Twilio create-message failed ({Scheduled}): {Reason}",
                scheduled ? "scheduled" : "immediate", DescribeStatus(ex));
            return new SmsSendResult(null, NotSentStatus, null, "Message could not be sent.");
        }
    }

    public async Task<SmsStatusResult?> GetStatusAsync(string providerMessageSid, CancellationToken ct)
    {
        try
        {
            return await Bounded(async token =>
            {
                var resp = await _client.Api20100401Message.FetchMessage(
                    accountSid: _options.AccountSid, sid: providerMessageSid, ct: token);
                return new SmsStatusResult(resp.Status?.Value, resp.ErrorCode, resp.ErrorMessage);
            }, ct);
        }
        catch (Exception ex)
        {
            // Best-effort read — keep the last known status rather than failing the read endpoint.
            _logger.LogWarning("Twilio fetch-message status failed for {Sid}: {Reason}",
                providerMessageSid, DescribeStatus(ex));
            return null;
        }
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct)
    {
        try
        {
            await Bounded(async token =>
            {
                await _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid, sid: providerMessageSid,
                    body: null, status: MessageEnumUpdateStatus.Canceled, ct: token);
                return true;
            }, ct);
        }
        catch (Exception ex)
        {
            throw Translate("cancel scheduled message", ex);
        }
    }

    public async Task DisposeContentAsync(string providerMessageSid, CancellationToken ct)
    {
        try
        {
            await Bounded(async token =>
            {
                // Empty body redacts the message text at the provider; the record and status survive.
                await _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid, sid: providerMessageSid,
                    body: string.Empty, status: null, ct: token);
                return true;
            }, ct);
        }
        catch (Exception ex)
        {
            throw Translate("dispose message content", ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<ProviderMessageRecord>();
        try
        {
            await Bounded(async token =>
            {
                int page = 0;
                string? pageToken = null;
                int pages = 0;

                while (true)
                {
                    var resp = await _client.Api20100401Message.ListMessage(
                        accountSid: _options.AccountSid,
                        to: null,
                        // Ask the provider for THIS number's messages, rather than filtering later.
                        from: _options.FromNumber,
                        dateSent: null,
                        dateSentQuery: to,          // wire DateSent<= : range upper bound
                        dateSentQueryQuery: from,   // wire DateSent>= : range lower bound
                        pageSize: PageSize,
                        page: page,
                        pageToken: pageToken,
                        ct: token);

                    if (resp.Messages is not null)
                    {
                        foreach (var m in resp.Messages)
                        {
                            results.Add(new ProviderMessageRecord(
                                m.Sid, m.Status?.Value, m.From, ParseDate(m.DateSent), m.ErrorCode));
                        }
                    }

                    if (string.IsNullOrEmpty(resp.NextPageUri) || ++pages >= MaxPages)
                    {
                        if (pages >= MaxPages && !string.IsNullOrEmpty(resp.NextPageUri))
                        {
                            _logger.LogWarning(
                                "Reconciliation stopped at the {MaxPages}-page cap; result may be partial.",
                                MaxPages);
                        }
                        break;
                    }

                    (page, pageToken) = ParseNextPage(resp.NextPageUri);
                }

                return true;
            }, ct);
        }
        catch (Exception ex)
        {
            throw Translate("list messages", ex);
        }

        return results;
    }

    // ----- helpers -----

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static (int page, string? pageToken) ParseNextPage(string nextPageUri)
    {
        int page = 0;
        string? token = null;
        var qIndex = nextPageUri.IndexOf('?');
        if (qIndex < 0)
        {
            return (page, token);
        }

        var query = nextPageUri.Substring(qIndex + 1);
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }
            var key = Uri.UnescapeDataString(pair.Substring(0, eq));
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
            {
                page = p;
            }
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                token = value;
            }
        }
        return (page, token);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>A short, PII-free description of an SDK/transport failure for logs.</summary>
    private static string DescribeStatus(Exception ex) => ex switch
    {
        SdkException<RawError> sdk => $"HTTP {(int)sdk.Error.StatusCode}",
        JsonException => "malformed provider response",
        TaskCanceledException => "timed out",
        HttpRequestException => "provider unreachable",
        _ => "unexpected error"
    };

    /// <summary>Translates an SDK/transport failure into a leak-free <see cref="SmsGatewayException"/>.</summary>
    private static SmsGatewayException Translate(string action, Exception ex) => ex switch
    {
        SdkException<RawError> sdk => new SmsGatewayException(
            $"Provider error while trying to {action}.", sdk.Error.StatusCode, ex),
        JsonException => new SmsGatewayException(
            $"The provider returned a response that could not be processed while trying to {action}.", null, ex),
        _ when ex is HttpRequestException or TaskCanceledException => new SmsGatewayException(
            $"The provider was unreachable while trying to {action}.", null, ex),
        _ => new SmsGatewayException($"Unexpected error while trying to {action}.", null, ex)
    };
}
