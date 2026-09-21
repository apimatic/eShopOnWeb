using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Adapts the vendored Twilio SDK to the application's <see cref="ISmsGateway"/> port.
///
/// Error boundary (all five operations are Case B — <c>SdkException&lt;RawError&gt;</c>): every call
/// catches the provider error, an unreadable body (<see cref="JsonException"/> — a drifted 2xx body, or
/// an error body that does not match), and transport failures (<see cref="HttpRequestException"/> /
/// <see cref="TaskCanceledException"/>). Send/schedule/fetch/cancel translate these into non-throwing
/// results so a messaging hiccup can never fail an order operation; validate/redact/list surface them as
/// <see cref="SmsGatewayException"/>.
///
/// The shopper's number is NEVER logged, and provider error bodies (which can echo the number) are never
/// logged or surfaced — only the HTTP status and the provider's numeric error code are.
/// </summary>
public sealed class TwilioSmsGateway : ISmsGateway
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    // A hard bound so a paged reconciliation sweep never relies on the provider's stop condition alone.
    private const int ReconcilePageSize = 200;
    private const int ReconcileMaxPages = 50;

    public TwilioSmsGateway(TwilioSdkClient client, TwilioOptions options, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public string SendingNumber => _options.FromNumber;

    public async Task<SmsValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct)
    {
        try
        {
            // Number lookup lives on a different provider host and is deliberately NOT governed by the
            // messaging base-URL override.
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null,
                ct: ct);

            if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
            {
                return new SmsValidationResult(true, response.PhoneNumber, null);
            }

            return new SmsValidationResult(false, null, "The number is not a usable destination.");
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("validate number", ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The number-lookup provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The number-lookup provider is unreachable.", null, ex);
        }
    }

    public Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken ct) =>
        CreateMessageAsync(toE164, body, from: _options.FromNumber, scheduleType: null, sendAt: null, messagingServiceSid: null, ct);

    public Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        // Scheduling requires a Messaging Service and no explicit From; the provider queues it for later.
        CreateMessageAsync(toE164, body, from: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, messagingServiceSid: _options.MessagingServiceSid, ct);

    private async Task<SmsDispatchResult> CreateMessageAsync(
        string toE164, string body, string? from, MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt, string? messagingServiceSid, CancellationToken ct)
    {
        try
        {
            var response = await _client.Api20100401Message.CreateMessage(
                accountSid: _options.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: from, fallbackFrom: null,
                messagingServiceSid: messagingServiceSid, body: body, mediaUrl: null, contentSid: null,
                ct: ct);

            var sid = response.Sid;
            var status = response.Status?.Value;
            if (string.IsNullOrEmpty(sid))
            {
                _logger.LogWarning("Provider accepted a message but returned no SID (status {Status}).", status ?? "(none)");
                return new SmsDispatchResult(false, null, status, response.ErrorCode, null, "Provider did not return a message identifier.");
            }

            _logger.LogInformation("Sent message {MessageSid} (status {Status}).", sid, status ?? "(none)");
            return new SmsDispatchResult(true, sid, status, response.ErrorCode, DescribeCode(response.ErrorCode), null);
        }
        catch (SdkException<RawError> ex)
        {
            var (status, code) = ReadStatusAndCode(ex);
            _logger.LogWarning("Message send failed: HTTP {Status}, provider code {Code}.", status, code?.ToString() ?? "(none)");
            return new SmsDispatchResult(false, null, null, code, DescribeCode(code), $"Provider rejected the send (HTTP {status}).");
        }
        catch (JsonException)
        {
            _logger.LogWarning("Message send returned a response that could not be processed.");
            return new SmsDispatchResult(false, null, null, null, null, "The provider returned an unreadable response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Message send could not reach the provider.");
            return new SmsDispatchResult(false, null, null, null, null, "The provider is unreachable.");
        }
    }

    public async Task<SmsStatusResult> FetchStatusAsync(string messageSid, CancellationToken ct)
    {
        try
        {
            var response = await _client.Api20100401Message.FetchMessage(_options.AccountSid, messageSid, ct: ct);
            return new SmsStatusResult(true, response.Status?.Value, response.ErrorCode, DescribeCode(response.ErrorCode));
        }
        catch (SdkException<RawError> ex)
        {
            var (status, _) = ReadStatusAndCode(ex);
            _logger.LogWarning("Could not refresh status for message {MessageSid}: HTTP {Status}.", messageSid, status);
            return new SmsStatusResult(false, null, null, null);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Could not refresh status for message {MessageSid}.", messageSid);
            return new SmsStatusResult(false, null, null, null);
        }
    }

    public async Task<bool> CancelScheduledAsync(string messageSid, CancellationToken ct)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid, sid: messageSid, body: null,
                status: MessageEnumUpdateStatus.Canceled, ct: ct);
            _logger.LogInformation("Cancelled scheduled message {MessageSid}.", messageSid);
            return true;
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Could not cancel scheduled message {MessageSid}.", messageSid);
            return false;
        }
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken ct)
    {
        try
        {
            // An empty body redacts the message text at the provider while keeping the record and outcome.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid, sid: messageSid, body: string.Empty, status: null, ct: ct);
            _logger.LogInformation("Redacted content of message {MessageSid}.", messageSid);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("redact message content", ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The provider returned a response that could not be processed while redacting.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The provider is unreachable while redacting.", null, ex);
        }
    }

    public async Task<SmsListResult> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var messages = new List<SmsProviderMessage>();
        var truncated = false;
        int page = 0;
        string? pageToken = null;

        try
        {
            while (true)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _options.AccountSid,
                    to: null,
                    from: _options.FromNumber,   // ask the provider for OUR sending number's messages only
                    dateSent: null,
                    dateSentQuery: to,           // DateSent< : upper bound of the range
                    dateSentQueryQuery: from,    // DateSent> : lower bound of the range
                    pageSize: ReconcilePageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: ct);

                var batch = response.Messages;
                if (batch is not null)
                {
                    foreach (var m in batch)
                    {
                        if (!string.IsNullOrEmpty(m.Sid))
                        {
                            messages.Add(new SmsProviderMessage(m.Sid!, m.Status?.Value, m.From, ParseRfc2822(m.DateSent), m.ErrorCode));
                        }
                    }
                }

                var count = batch?.Count ?? 0;
                if (count < ReconcilePageSize || string.IsNullOrEmpty(response.NextPageUri))
                {
                    break; // no further pages
                }

                page++;
                if (page >= ReconcileMaxPages)
                {
                    truncated = true;
                    _logger.LogWarning("Reconciliation sweep capped at {MaxPages} pages; result is partial.", ReconcileMaxPages);
                    break;
                }

                pageToken = ExtractQueryValue(response.NextPageUri, "PageToken");
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("list messages", ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The provider returned a message list that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The provider is unreachable while listing messages.", null, ex);
        }

        return new SmsListResult(messages, truncated);
    }

    // --- error / parsing helpers -------------------------------------------------

    private SmsGatewayException ToGatewayException(string action, SdkException<RawError> ex)
    {
        var (status, code) = ReadStatusAndCode(ex);
        // Deliberately no response body in the message — a provider error body can echo the phone number.
        var detail = code is null ? $"HTTP {status}" : $"HTTP {status}, provider code {code}";
        _logger.LogWarning("Provider error on {Action}: {Detail}.", action, detail);
        return new SmsGatewayException($"The messaging provider could not {action} ({detail}).", status, ex);
    }

    private static (int Status, int? Code) ReadStatusAndCode(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        int? code = null;
        try
        {
            // Read only the numeric provider code — never the message, which can carry the number.
            var body = ex.Error.ReadAsJson<TwilioErrorBody>();
            code = body?.Code;
        }
        catch (JsonException)
        {
            // Body was not JSON (e.g. a gateway HTML page) — status alone is what we carry.
        }
        return (status, code);
    }

    private static string? DescribeCode(int? code) =>
        code is null ? null : $"Provider error code {code}.";

    private static DateTimeOffset? ParseRfc2822(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    /// <summary>Extract a query-string value from a (possibly relative) provider URI.</summary>
    private static string? ExtractQueryValue(string? uri, string key)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return null;
        }

        var queryStart = uri!.IndexOf('?');
        if (queryStart < 0 || queryStart == uri.Length - 1)
        {
            return null;
        }

        var query = uri.Substring(queryStart + 1);
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var name = Uri.UnescapeDataString(pair.Substring(0, eq));
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
        }
        return null;
    }

    private sealed record TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }

        [JsonPropertyName("status")]
        public int? Status { get; init; }
    }
}
