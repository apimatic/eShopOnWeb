using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio-backed <see cref="ISmsProvider"/>. Every SDK exception (typed/raw error, transport failure, or a
/// deserialization failure on a drifted body) is converted to a single <see cref="SmsProviderException"/> so
/// callers handle one type. Phone numbers and message bodies are never written to logs here.
/// </summary>
public sealed class TwilioSmsProvider : ISmsProvider
{
    // Total per-call budget. The SDK's own Timeout is per-attempt; a CancellationToken deadline is the only
    // thing that bounds a whole call, so every SDK call gets this linked deadline.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Absolute backstop so a provider that keeps handing out a next page can never spin forever.
    private const int MaxReconciliationPages = 10_000;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(TwilioSdkClient client, TwilioSettings settings, IAppLogger<TwilioSmsProvider> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct)
    {
        using var scope = Budget(ct);
        try
        {
            var resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: scope.Token);

            var valid = resp.Valid == true;
            return new PhoneValidationResult(
                valid,
                valid ? resp.PhoneNumber : null,
                valid ? null : "The provider does not consider this number a usable destination.");
        }
        catch (Exception ex)
        {
            throw Translate(ex, isWrite: false);
        }
    }

    public async Task<SentSms> SendAsync(string toE164, string body, CancellationToken ct)
    {
        using var scope = Budget(ct);
        try
        {
            var resp = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null, from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: scope.Token);

            return ToSentSms(resp);
        }
        catch (Exception ex)
        {
            throw Translate(ex, isWrite: true);
        }
    }

    public async Task<SentSms> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        using var scope = Budget(ct);
        try
        {
            // Scheduling is Messaging-Service only: set the service SID + scheduleType=fixed + sendAt, and no from.
            var resp = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: scope.Token);

            return ToSentSms(resp);
        }
        catch (Exception ex)
        {
            throw Translate(ex, isWrite: true);
        }
    }

    public async Task<SentSms> CancelScheduledAsync(string providerSid, CancellationToken ct)
    {
        using var scope = Budget(ct);
        try
        {
            var resp = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: scope.Token);

            return ToSentSms(resp);
        }
        catch (Exception ex)
        {
            throw Translate(ex, isWrite: true);
        }
    }

    public async Task RedactAsync(string providerSid, CancellationToken ct)
    {
        using var scope = Budget(ct);
        try
        {
            // An empty body redacts the message text at the provider while the record and outcome survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: string.Empty,
                status: null,
                ct: scope.Token);
        }
        catch (Exception ex)
        {
            throw Translate(ex, isWrite: true);
        }
    }

    public async Task<SentSms?> FetchAsync(string providerSid, CancellationToken ct)
    {
        using var scope = Budget(ct);
        try
        {
            var resp = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                ct: scope.Token);

            return ToSentSms(resp);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw Translate(ex, isWrite: false);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var scope = Budget(ct);
        var results = new List<ProviderMessageRecord>();
        int? page = null;
        string? pageToken = null;
        var pages = 0;

        try
        {
            while (true)
            {
                // Ask the provider for THIS number's messages in range (DateSent> lower, DateSent< upper).
                var resp = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,        // wire DateSent< : upper bound
                    dateSentQueryQuery: from, // wire DateSent> : lower bound
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: scope.Token);

                if (resp.Messages is not null)
                {
                    foreach (var m in resp.Messages)
                    {
                        if (m.Sid is null) continue;
                        results.Add(new ProviderMessageRecord(
                            m.Sid, m.Status?.Value, m.To, m.From, ParseProviderDate(m.DateSent)));
                    }
                }

                if (string.IsNullOrEmpty(resp.NextPageUri)) break;
                if (++pages >= MaxReconciliationPages)
                {
                    _logger.LogWarning($"Reconciliation stopped at the {MaxReconciliationPages}-page backstop; result may be partial.");
                    break;
                }

                (page, pageToken) = ParseNextPage(resp.NextPageUri);
                if (pageToken is null && page is null) break; // cannot advance safely
            }
        }
        catch (Exception ex)
        {
            throw Translate(ex, isWrite: false);
        }

        return results;
    }

    // ---- helpers ----

    private static SentSms ToSentSms(ApiV2010AccountMessage m) =>
        new(m.Sid, m.Status?.Value, ParseProviderDate(m.DateSent), m.ErrorCode, m.ErrorMessage);

    private static DateTimeOffset? ParseProviderDate(string? rfc2822)
    {
        if (string.IsNullOrWhiteSpace(rfc2822)) return null;
        return DateTimeOffset.TryParse(rfc2822, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        var q = nextPageUri.IndexOf('?');
        if (q < 0) return (null, null);
        int? page = null;
        string? token = null;
        foreach (var pair in nextPageUri[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var key = pair[..eq];
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p)) page = p;
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase)) token = value;
        }
        return (page, token);
    }

    private CancellationScope Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return new CancellationScope(cts);
    }

    /// <summary>
    /// Converts any SDK/transport/deserialization failure into an <see cref="SmsProviderException"/>. A
    /// write whose transport failed may already have been received, so its outcome is unknown rather than a
    /// definite failure. No phone number or body is included in the message.
    /// </summary>
    private SmsProviderException Translate(Exception ex, bool isWrite)
    {
        switch (ex)
        {
            case SdkException<RawError> sdk:
                var status = sdk.Error.StatusCode;
                _logger.LogWarning($"Twilio returned HTTP {(int)status}.");
                // A provider HTTP response means the request was received; not an unknown outcome.
                return new SmsProviderException($"The messaging provider returned HTTP {(int)status}.", status, outcomeUnknown: false, ex);

            case JsonException:
                // A drifted 2xx body, or an error body that didn't match its shape. Treat a write as unknown.
                return new SmsProviderException("The messaging provider returned a response that could not be processed.",
                    statusCode: null, outcomeUnknown: isWrite, ex);

            case OperationCanceledException:
            case HttpRequestException:
                // Nothing (or an unconfirmed something) reached the provider.
                return new SmsProviderException("The messaging provider could not be reached.",
                    statusCode: null, outcomeUnknown: isWrite, ex);

            default:
                return new SmsProviderException("Unexpected messaging provider failure.",
                    statusCode: null, outcomeUnknown: isWrite, ex);
        }
    }

    private readonly struct CancellationScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        public CancellationScope(CancellationTokenSource cts) => _cts = cts;
        public CancellationToken Token => _cts.Token;
        public void Dispose() => _cts.Dispose();
    }
}
