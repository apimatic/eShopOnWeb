using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// <see cref="ISmsSender"/> over the Twilio .NET SDK. Every call is bounded by a whole-call deadline and
/// every provider fault (error status, unreachable, or an unprocessable response) is translated to
/// <see cref="SmsGatewayException"/>. Message content and phone numbers are never logged, and provider
/// error bodies (which can echo the destination number) are reduced to a status + numeric code.
/// </summary>
public class TwilioMessagingGateway : ISmsSender
{
    // Whole-call budget. The SDK's own Timeout is per-attempt; this CancellationToken bounds the whole call.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ReconciliationPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioMessagingGateway> _logger;

    public TwilioMessagingGateway(
        TwilioSdkClient client,
        IOptions<TwilioOptions> options,
        IAppLogger<TwilioMessagingGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken) =>
        RunAsync("lookup", async ct =>
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: ct);

            var isValid = response.Valid == true;
            return new PhoneValidationResult(isValid, response.PhoneNumber, response.CountryCode);
        }, cancellationToken);

    public Task<SmsMessageResult> SendAsync(string toE164, string body, CancellationToken cancellationToken) =>
        RunAsync("send", async ct =>
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _options.AccountSid,
                to: toE164,
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
                from: _options.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: ct);

            return ToResult(message, DateTimeOffset.UtcNow);
        }, cancellationToken);

    public Task<SmsMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken) =>
        RunAsync("schedule", async ct =>
        {
            // Scheduled messages must go through a Messaging Service with ScheduleType=fixed and SendAt set,
            // and must not carry an explicit From — per the provider's scheduling contract.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _options.AccountSid,
                to: toE164,
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
                sendAt: sendAtUtc,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: null,
                fallbackFrom: null,
                messagingServiceSid: _options.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: ct);

            return ToResult(message, sendAtUtc);
        }, cancellationToken);

    public Task<SmsMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken) =>
        RunAsync("cancel-scheduled", async ct =>
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);

            return ToResult(message, DateTimeOffset.UtcNow);
        }, cancellationToken);

    public Task<SmsMessageResult> GetStatusAsync(string messageSid, CancellationToken cancellationToken) =>
        RunAsync("fetch", async ct =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: messageSid,
                ct: ct);

            return ToResult(message, DateTimeOffset.UtcNow);
        }, cancellationToken);

    public Task RedactContentAsync(string messageSid, CancellationToken cancellationToken) =>
        RunAsync("redact", async ct =>
        {
            // An empty (non-null) body is what redacts the text at the provider; a null body would be
            // dropped from the form and change nothing. The message record and its status survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: messageSid,
                body: string.Empty,
                status: null,
                ct: ct);

            return true;
        }, cancellationToken);

    public Task<ProviderMessagePage> ListSentMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int? page,
        string? pageToken,
        CancellationToken cancellationToken) =>
        RunAsync("list", async ct =>
        {
            // Ask the provider for THIS application's sending number only (From), bounded by the send-time
            // range: DateSent> (from) ← dateSentQueryQuery, DateSent< (to) ← dateSentQuery.
            var response = await _client.Api20100401Message.ListMessage(
                accountSid: _options.AccountSid,
                to: null,
                from: _options.FromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: ReconciliationPageSize,
                page: page,
                pageToken: pageToken,
                ct: ct);

            var messages = new List<ProviderMessage>();
            if (response.Messages is not null)
            {
                foreach (var m in response.Messages)
                {
                    DateTimeOffset? dateSent = null;
                    if (!string.IsNullOrEmpty(m.DateSent)
                        && DateTimeOffset.TryParse(m.DateSent, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                    {
                        dateSent = parsed;
                    }

                    messages.Add(new ProviderMessage(m.Sid, m.Status?.Value, m.To, dateSent));
                }
            }

            var (nextPage, nextToken) = ParseNextPage(response.NextPageUri);
            var hasMore = !string.IsNullOrEmpty(response.NextPageUri);
            return new ProviderMessagePage(messages, nextPage, nextToken, hasMore);
        }, cancellationToken);

    private static SmsMessageResult ToResult(TwilioSdk.Models.ApiV2010AccountMessage message, DateTimeOffset sentAtUtc) =>
        new(message.Sid, message.Status?.Value, message.ErrorCode, message.ErrorMessage, sentAtUtc);

    private static (int? Page, string? PageToken) ParseNextPage(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return (null, null);
        }

        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0)
        {
            return (null, null);
        }

        int? page = null;
        string? token = null;
        var pairs = nextPageUri.Substring(queryIndex + 1).Split('&');
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = pair.Substring(0, eq);
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
            {
                page = p;
            }
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                token = value;
            }
        }

        return (page, token);
    }

    /// <summary>
    /// Runs one SDK call under a whole-call deadline and translates every provider fault into
    /// <see cref="SmsGatewayException"/>. Provider error bodies are reduced to status + numeric code so a
    /// destination number that the body may echo is never surfaced or logged.
    /// </summary>
    private async Task<T> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var status = ex.Error.StatusCode;
            var code = TryExtractErrorCode(ex.Error);
            _logger.LogWarning("Twilio {Operation} failed with HTTP {Status}{Code}.",
                operation, (int)status, code is null ? string.Empty : $" (code {code})");
            throw new SmsGatewayException($"Twilio {operation} returned HTTP {(int)status}.", status, ex);
        }
        catch (JsonException ex)
        {
            // A 2xx whose body drifted, or an error body that did not match the generated shape.
            _logger.LogWarning("Twilio {Operation} returned an unprocessable response.", operation);
            throw new SmsGatewayException($"Twilio {operation} returned a response that could not be processed.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own deadline elapsed (not the caller cancelling).
            _logger.LogWarning("Twilio {Operation} timed out.", operation);
            throw new SmsGatewayException($"Twilio {operation} timed out.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Twilio {Operation} could not reach the provider.", operation);
            throw new SmsGatewayException($"Twilio {operation} could not reach the provider.", ex);
        }
    }

    private static int? TryExtractErrorCode(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.Number
                && codeElement.TryGetInt32(out var code))
            {
                return code;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (proxy/gateway HTML, etc.) — nothing to extract.
        }

        return null;
    }
}
