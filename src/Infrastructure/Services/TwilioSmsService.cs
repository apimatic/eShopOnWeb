using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio-backed ISmsService. All SDK failures are translated to SmsProviderException with
/// caller-safe messages; destination numbers and message bodies are never logged.
/// </summary>
public class TwilioSmsService : ISmsService
{
    public const string HttpClientName = "Twilio";

    // Whole-call budget: every provider call is bounded, and a handler making several
    // calls passes the same request token so the costs add up against one deadline.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // 50 pages x 100 messages = 5000 per report; hitting the cap is surfaced, never silent.
    private const int ListPageSize = 100;
    private const int MaxListPages = 50;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioSmsService> _logger;

    public TwilioSmsService(TwilioSdkClient client, IOptions<TwilioOptions> options, IAppLogger<TwilioSmsService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var lookup = await Bounded(c => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
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
                requestOptions: null,
                ct: c), ct);

            var isValid = lookup.Valid == true;
            string? reason = null;
            if (!isValid)
            {
                reason = lookup.ValidationErrors is { Count: > 0 }
                    ? string.Join(", ", lookup.ValidationErrors.Select(e => e.Value))
                    : "the provider does not consider it valid";
            }

            return new PhoneNumberValidationResult(isValid, isValid ? lookup.PhoneNumber : null, reason);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider answers some invalid number shapes with 404 — that is a
            // "not usable" answer, not a fault.
            return new PhoneNumberValidationResult(false, null, "the provider does not consider it valid");
        }
        catch (Exception ex)
        {
            throw Translate(ex, "validate a phone number");
        }
    }

    public Task<SmsSendResult> SendSmsAsync(string to, string body, CancellationToken ct = default) =>
        CreateMessageAsync(to, body, scheduledFor: null, ct);

    public Task<SmsSendResult> ScheduleSmsAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default) =>
        CreateMessageAsync(to, body, sendAt, ct);

    public async Task CancelScheduledSmsAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            await Bounded(c => _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: c), ct);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "cancel a scheduled message");
        }
    }

    public async Task<SmsMessageStatusResult> GetMessageStatusAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: messageSid,
                requestOptions: null,
                ct: c), ct);

            return new SmsMessageStatusResult(
                message.Status?.Value,
                message.ErrorCode,
                message.ErrorMessage,
                ParseProviderDate(message.DateSent));
        }
        catch (Exception ex)
        {
            throw Translate(ex, "read a message status");
        }
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            // Empty body erases the text; the message record and its status survive.
            await Bounded(c => _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: messageSid,
                body: "",
                status: null,
                requestOptions: null,
                ct: c), ct);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "dispose of message content");
        }
    }

    public async Task<ProviderSmsListResult> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var messages = new List<ProviderSmsRecord>();
        var truncated = false;
        string? pageToken = null;
        var page = 0;

        try
        {
            while (true)
            {
                // Twilio no longer supports page-number paging ("Page" is rejected with
                // error 20001): the first page goes out with no cursor, later pages follow
                // the PageToken cursor carried in next_page_uri.
                var currentToken = pageToken;
                var response = await Bounded(c => _client.Api20100401Message.ListMessage(
                    accountSid: _options.AccountSid,
                    to: null,
                    from: _options.FromNumber,
                    dateSent: null,
                    dateSentQuery: toUtc,          // wire: DateSent< (strictly before)
                    dateSentQueryQuery: fromUtc,   // wire: DateSent> (strictly after)
                    pageSize: ListPageSize,
                    page: null,
                    pageToken: currentToken,
                    requestOptions: null,
                    ct: c), ct);

                if (response.Messages is { Count: > 0 } pageMessages)
                {
                    messages.AddRange(pageMessages.Select(m => new ProviderSmsRecord(
                        m.Sid ?? string.Empty,
                        m.To,
                        m.From,
                        m.Status?.Value,
                        ParseProviderDate(m.DateSent),
                        m.ErrorCode,
                        m.ErrorMessage)));
                }

                pageToken = ExtractPageToken(response.NextPageUri);
                if (pageToken is null)
                {
                    break;
                }

                page++;
                if (page >= MaxListPages)
                {
                    truncated = true;
                    _logger.LogWarning("Twilio message list hit the {MaxPages}-page cap; the range may be incomplete.", MaxListPages);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            throw Translate(ex, "list messages for reconciliation");
        }

        return new ProviderSmsListResult(messages, _options.FromNumber, truncated);
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri) || !Uri.TryCreate(nextPageUri, UriKind.Absolute, out var uri))
        {
            return null;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private async Task<SmsSendResult> CreateMessageAsync(string to, string body, DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        // Exactly one sender identity per call: immediate sends go from the configured
        // FromNumber (so reconciliation's server-side From filter finds them); scheduled
        // sends go through the messaging service (scheduling is messaging-service only).
        using var scope = SingleSendScope.Enter();
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.CreateMessage(
                accountSid: _options.AccountSid,
                to: to,
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
                scheduleType: scheduledFor is null ? null : MessageEnumScheduleType.Fixed,
                sendAt: scheduledFor,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: scheduledFor is null ? _options.FromNumber : null,
                fallbackFrom: null,
                messagingServiceSid: scheduledFor is null ? null : _options.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: c), ct);

            if (message.Sid is null)
            {
                throw new SmsProviderException("The provider accepted the message but returned no identifier.");
            }

            return new SmsSendResult(message.Sid, message.Status?.Value);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "send a message");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private SmsProviderException Translate(Exception ex, string operation) => ex switch
    {
        SmsProviderException already => already,
        DuplicateSendBlockedException blocked => new SmsProviderException(
            $"The request to {operation} may already have reached the provider; the duplicate attempt was stopped. Settle the outcome by reading provider state.",
            null, blocked),
        SdkException<RawError> api => new SmsProviderException(ProviderMessage(api, operation), api.Error.StatusCode, api),
        JsonException json => new SmsProviderException(
            $"The provider returned a response that could not be processed while trying to {operation}.", null, json),
        _ when ex is HttpRequestException or TaskCanceledException => new SmsProviderException(
            $"The provider could not be reached while trying to {operation}.", null, ex),
        _ => new SmsProviderException($"Unexpected failure while trying to {operation}.", null, ex)
    };

    private static string ProviderMessage(SdkException<RawError> ex, string operation)
    {
        var status = (int)ex.Error.StatusCode;
        return status switch
        {
            // Our credentials or our quota — the caller did nothing wrong; no detail echoed.
            401 or 403 => $"The provider rejected our credentials while trying to {operation} (HTTP {status}).",
            429 => $"The provider rate-limited the request to {operation} (HTTP 429).",
            // The provider rejected the request itself — hand the caller the actionable detail.
            >= 400 and < 500 => $"The provider rejected the request to {operation} (HTTP {status}): {TruncatedBody(ex)}",
            _ => $"The provider failed the request to {operation} (HTTP {status})."
        };
    }

    private static string TruncatedBody(SdkException<RawError> ex)
    {
        string? body;
        try
        {
            body = ex.Error.ReadAsString();
        }
        catch
        {
            return "no detail available";
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return "no detail available";
        }

        const int max = 300;
        return body.Length <= max ? body : body[..max] + "…";
    }

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
