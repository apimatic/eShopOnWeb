using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.Notifications;

/// <summary>
/// Twilio-backed implementation of <see cref="ISmsGateway"/>. This is the only place that knows about the
/// Twilio SDK. It never logs a shopper's destination number or a message body — only SIDs, statuses, counts
/// and dates. Genuine provider/transport failures are allowed to surface to the caller (the notification
/// orchestrator catches them so an SMS failure never fails the order); the sole exception is a lookup of a
/// number the provider does not recognise, which is a domain "invalid", not a failure.
/// </summary>
public sealed class TwilioSmsGateway : ISmsGateway
{
    private const long PageSize = 50;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> options,
        IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null,
                countryCode: null,
                firstName: null,
                lastName: null,
                addressLine1: null,
                addressLine2: null,
                city: null,
                state: null,
                postalCode: null,
                addressCountryCode: null,
                nationalId: null,
                dateOfBirth: null,
                lastVerifiedDate: null,
                verificationSid: null,
                partnerSubId: null,
                ct: cancellationToken);

            // resp.Valid is the provider's usability flag; resp.PhoneNumber is the canonical returned number.
            return new PhoneLookupResult(resp.Valid ?? false, resp.PhoneNumber);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider does not recognise the number: a usable-destination miss, not a failure.
            // Deliberately narrow — a JsonException (unreadable body) is NOT caught here, so a corrupt
            // response can never be mistaken for a genuine "invalid number".
            _logger.LogInformation("Phone lookup returned {StatusCode}; number treated as not usable.", (int)ex.Error.StatusCode);
            return new PhoneLookupResult(false, null);
        }
    }

    public async Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var resp = await _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: toNumber,
            statusCallback: null,
            applicationSid: null,
            maxPrice: null,
            provideFeedback: null,
            attempt: null,
            validityPeriod: null,
            forceDelivery: null,
            contentRetention: null,
            addressRetention: null,
            smartEncoded: null,
            persistentAction: null,
            trafficType: null,
            shortenUrls: null,
            scheduleType: null,
            sendAt: null,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: null,
            body: body,
            mediaUrl: null,
            contentSid: null,
            ct: cancellationToken);

        var status = resp.Status?.Value ?? string.Empty;
        _logger.LogInformation("Sent SMS; sid {Sid} status {Status}.", resp.Sid, status);
        return new SmsSendResult(resp.Sid!, status);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging-Services-only feature: schedule_type=fixed + a future send_at, sent via
        // the configured Messaging Service (not a From number).
        var resp = await _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: toNumber,
            statusCallback: null,
            applicationSid: null,
            maxPrice: null,
            provideFeedback: null,
            attempt: null,
            validityPeriod: null,
            forceDelivery: null,
            contentRetention: null,
            addressRetention: null,
            smartEncoded: null,
            persistentAction: null,
            trafficType: null,
            shortenUrls: null,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: null,
            fallbackFrom: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            body: body,
            mediaUrl: null,
            contentSid: null,
            ct: cancellationToken);

        var status = resp.Status?.Value ?? string.Empty;
        _logger.LogInformation("Scheduled SMS; sid {Sid} status {Status} for {SendAt}.", resp.Sid, status, sendAt);
        return new SmsSendResult(resp.Sid!, status);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: cancellationToken);

        _logger.LogInformation("Cancelled scheduled message {Sid}.", messageSid);
    }

    public async Task<string> GetStatusAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var resp = await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            ct: cancellationToken);

        var status = resp.Status?.Value ?? string.Empty;
        _logger.LogInformation("Fetched status {Status} for message {Sid}.", status, messageSid);
        return status;
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Empty-string body is load-bearing: it redacts the stored content while keeping the record.
        // Passing null would leave the body unchanged.
        await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            body: string.Empty,
            status: null,
            ct: cancellationToken);

        _logger.LogInformation("Redacted body for message {Sid}.", messageSid);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();

        // Server-side filter by the configured From number + a DateSent range. This op has no generated
        // auto-pager, so we follow the provider's own next_page_uri link, re-passing the same From/date
        // filters on every page, until the provider stops returning a next page.
        int? page = 0;
        string? pageToken = null;

        while (true)
        {
            var response = await _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: to,          // DateSent< : sent on/before the upper bound
                dateSentQueryQuery: from,   // DateSent> : sent on/after the lower bound
                pageSize: PageSize,
                page: page,
                pageToken: pageToken,
                ct: cancellationToken);

            if (response.Messages is { Count: > 0 })
            {
                foreach (var m in response.Messages)
                {
                    results.Add(new ProviderMessage(
                        m.Sid!,
                        m.From,
                        m.Status?.Value ?? string.Empty,
                        ParseDateSent(m.DateSent)));
                }
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }

            var (nextPage, nextToken) = ParsePaging(response.NextPageUri);
            if (nextPage is null && nextToken is null)
            {
                break; // no advanceable paging info — stop rather than risk an infinite loop
            }

            page = nextPage ?? (page + 1);
            pageToken = nextToken;
        }

        _logger.LogInformation(
            "Listed {Count} provider messages in range {From}..{To}.",
            results.Count, from, to);

        return results;
    }

    /// <summary>Best-effort parse of the provider's string DateSent; null when absent or unparseable.</summary>
    private static DateTimeOffset? ParseDateSent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Extract the Page and PageToken query values from a provider next-page URI.</summary>
    private static (int? Page, string? Token) ParsePaging(string uri)
    {
        var q = uri.IndexOf('?');
        if (q < 0)
        {
            return (null, null);
        }

        int? page = null;
        string? token = null;

        foreach (var pair in uri[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = pair[..eq];
            var val = Uri.UnescapeDataString(pair[(eq + 1)..]);

            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
            {
                page = p;
            }
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                token = val;
            }
        }

        return (page, token);
    }
}
