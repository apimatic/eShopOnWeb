using System;
using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The one and only place the Twilio SDK is called. Every operation is bounded by a whole-call deadline,
/// and every SDK/transport failure is translated to <see cref="TwilioProviderException"/> so the rest of
/// the app sees a single failure type. Nothing here logs the destination number or the message body.
/// </summary>
public sealed class TwilioMessagingGateway : ISmsGateway
{
    // Whole-call budget. Each SDK call's per-attempt Retry.Timeout is set lower at registration; this is
    // the ceiling the caller actually experiences for one gateway operation.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    private const int MaxReconciliationPages = 50;
    private const long ReconciliationPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PhoneNumberLookupResult> LookupNumberAsync(string phoneNumber, CancellationToken ct)
    {
        using var scope = Bound(ct, out var token);
        try
        {
            // Lookups is served from its own host (server group Default4); the Twilio:BaseUrl messaging
            // override does not apply to it. All 15 optional lookup params are omitted (null).
            var resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: token);

            return new PhoneNumberLookupResult(resp.Valid ?? false, resp.PhoneNumber);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            // A number the provider cannot resolve at all — not a usable destination, not a fault.
            return new PhoneNumberLookupResult(false, null);
        }
        catch (Exception ex)
        {
            throw Translate("lookup number", ex, ct);
        }
    }

    public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct) =>
        CreateMessageAsync(to, body, from: _settings.FromNumber, messagingServiceSid: null,
            scheduleType: null, sendAt: null, "send", ct);

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt,
        CancellationToken ct) =>
        // Scheduled messages must go via a Messaging Service (provider requirement), not a raw From.
        CreateMessageAsync(to, body, from: null, messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, "schedule", ct);

    private async Task<SmsSendResult> CreateMessageAsync(string to, string body, string? from,
        string? messagingServiceSid, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt,
        string operation, CancellationToken ct)
    {
        using var scope = Bound(ct, out var token);
        try
        {
            var resp = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: to,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: from, fallbackFrom: null,
                messagingServiceSid: messagingServiceSid, body: body, mediaUrl: null, contentSid: null,
                ct: token);

            if (string.IsNullOrEmpty(resp.Sid))
            {
                throw new TwilioProviderException($"Twilio {operation} returned no message id", null, null);
            }

            return new SmsSendResult(resp.Sid!, resp.Status?.Value, resp.ErrorCode, resp.DateSent);
        }
        catch (TwilioProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(operation, ex, ct);
        }
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken ct)
    {
        using var scope = Bound(ct, out var token);
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: messageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled, ct: token);
        }
        catch (Exception ex)
        {
            throw Translate("cancel scheduled", ex, ct);
        }
    }

    public async Task RedactAsync(string messageSid, CancellationToken ct)
    {
        using var scope = Bound(ct, out var token);
        try
        {
            // Empty body redacts the message text at the provider while leaving the record and its status.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: messageSid,
                body: string.Empty, status: null, ct: token);
        }
        catch (Exception ex)
        {
            throw Translate("redact", ex, ct);
        }
    }

    public async Task<ProviderMessageStatus> FetchAsync(string messageSid, CancellationToken ct)
    {
        using var scope = Bound(ct, out var token);
        try
        {
            var resp = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: messageSid, ct: token);
            return new ProviderMessageStatus(resp.Status?.Value, resp.ErrorCode, resp.DateSent);
        }
        catch (Exception ex)
        {
            throw Translate("fetch message", ex, ct);
        }
    }

    public async Task<ProviderMessageList> ListSentFromConfiguredSenderAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        using var scope = Bound(ct, out var token);
        try
        {
            var messages = new List<ProviderMessage>();
            var truncated = false;
            int? page = 0;
            string? pageToken = null;

            while (true)
            {
                // Ask the provider only for THIS sender's messages, over the range, on the provider's own
                // send-time clock (DateSent> = from, DateSent< = to).
                var resp = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,          // wire DateSent<  (range end)
                    dateSentQueryQuery: from,   // wire DateSent>  (range start)
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: token);

                if (resp.Messages != null)
                {
                    foreach (var m in resp.Messages)
                    {
                        if (!string.IsNullOrEmpty(m.Sid))
                        {
                            messages.Add(new ProviderMessage(m.Sid!, m.Status?.Value, m.To, m.From,
                                m.DateSent, m.ErrorCode));
                        }
                    }
                }

                if (string.IsNullOrEmpty(resp.NextPageUri))
                {
                    break; // provider signalled the end of the range
                }

                if (messages.Count > 0 && (page ?? 0) + 1 >= MaxReconciliationPages)
                {
                    truncated = true; // page cap reached before the provider signalled the end
                    break;
                }

                (page, pageToken) = NextPaging(resp.NextPageUri, page);
            }

            return new ProviderMessageList(messages, truncated);
        }
        catch (Exception ex)
        {
            throw Translate("list messages", ex, ct);
        }
    }

    /// <summary>Extract the Page/PageToken the provider's next_page_uri wants for the following request.</summary>
    private static (int? page, string? pageToken) NextPaging(string nextPageUri, int? currentPage)
    {
        int? page = (currentPage ?? 0) + 1;
        string? token = null;

        var q = nextPageUri.IndexOf('?');
        if (q >= 0)
        {
            foreach (var pair in nextPageUri.Substring(q + 1).Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                var key = pair.Substring(0, eq);
                var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
                if (string.Equals(key, "Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
                {
                    page = p;
                }
                else if (string.Equals(key, "PageToken", StringComparison.OrdinalIgnoreCase))
                {
                    token = value;
                }
            }
        }

        return (page, token);
    }

    /// <summary>Create a linked token that also expires after the whole-call budget.</summary>
    private static IDisposable Bound(CancellationToken ct, out CancellationToken token)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        token = cts.Token;
        return cts;
    }

    /// <summary>Translate any SDK/transport failure into the single provider exception type.</summary>
    private static TwilioProviderException Translate(string operation, Exception ex, CancellationToken outerCt)
    {
        switch (ex)
        {
            case SdkException<RawError> sdk:
                // Case B: RawError carries the HTTP status directly.
                return new TwilioProviderException(
                    $"Twilio {operation} failed (HTTP {(int)sdk.Error.StatusCode})", sdk.Error.StatusCode, sdk);

            case JsonException:
                // A drifted 2xx body, or a non-2xx body that didn't match the error shape — either way the
                // status is gone and the body is unprocessable.
                return new TwilioProviderException(
                    $"Twilio {operation} returned an unprocessable response", null, ex);

            case OperationCanceledException when outerCt.IsCancellationRequested:
                // The caller's request was aborted — surface as cancellation, not a provider fault.
                throw ex;

            case OperationCanceledException:
                // Our own whole-call deadline fired.
                return new TwilioProviderException($"Twilio {operation} timed out", null, ex);

            case HttpRequestException:
                return new TwilioProviderException($"Twilio {operation} unreachable", null, ex);

            default:
                return new TwilioProviderException($"Twilio {operation} failed", null, ex);
        }
    }
}
