using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

/// <summary>
/// The only type in the application that touches TwilioSdk.* types. Every call is
/// bounded by one deadline, and every failure is translated to
/// <see cref="TwilioProviderException"/> carrying a caller-safe message.
/// Phone numbers and message bodies are never written to logs.
/// </summary>
public class TwilioMessaging : ITwilioMessaging
{
    internal const string HttpClientName = "Twilio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CancelNotFoundRetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxCancelAttempts = 4;
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessaging> _logger;

    public TwilioMessaging(TwilioSdkClient client, IOptions<TwilioOptions> options, ILogger<TwilioMessaging> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber,
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
                ct: token), ct);

            var errors = response.ValidationErrors?.Select(e => e.Value).ToArray() ?? Array.Empty<string>();
            var isValid = response.Valid == true;
            return new ValidatedPhoneNumber(isValid, isValid ? response.PhoneNumber : null, errors);
        }
        catch (SdkException<RawError> ex) when (IsNumberRejection(ex.Error.StatusCode))
        {
            // Defensive: a malformed number may surface as a non-2xx rejection instead of 200 + valid:false.
            _logger.LogWarning("Twilio number validation rejected the number: HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            return new ValidatedPhoneNumber(false, null, new[] { $"rejected (HTTP {(int)ex.Error.StatusCode})" });
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(nameof(ValidatePhoneNumberAsync), ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(nameof(ValidatePhoneNumberAsync), ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(nameof(ValidatePhoneNumberAsync), ex);
        }
    }

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(token => _client.Api20100401Message.CreateMessage(
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
                requestOptions: null,
                ct: token), ct);

            return ToProviderMessage(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(nameof(SendMessageAsync), ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(nameof(SendMessageAsync), ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(nameof(SendMessageAsync), ex);
        }
    }

    public async Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAtUtc, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(token => _client.Api20100401Message.CreateMessage(
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
                scheduleType: MessageEnumScheduleType.Fixed,
                sendAt: sendAtUtc.ToUniversalTime(),
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: null,
                fallbackFrom: null,
                messagingServiceSid: _options.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: token), ct);

            return ToProviderMessage(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(nameof(ScheduleMessageAsync), ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(nameof(ScheduleMessageAsync), ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(nameof(ScheduleMessageAsync), ex);
        }
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken ct)
    {
        // A message scheduled moments ago can briefly 404 on update before the provider's
        // record becomes consistent. A cancelled order's follow-up must never go out, so
        // that one case is retried on a short fuse; every other failure translates as usual.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var message = await Bounded(token => _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid,
                    sid: providerMessageSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    requestOptions: null,
                    ct: token), ct);

                return ToProviderMessage(message);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound
                && attempt < MaxCancelAttempts)
            {
                await Task.Delay(CancelNotFoundRetryDelay, ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw Translate(nameof(CancelScheduledMessageAsync), ex);
            }
            catch (JsonException ex)
            {
                throw Unprocessable(nameof(CancelScheduledMessageAsync), ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw Unreachable(nameof(CancelScheduledMessageAsync), ex);
            }
        }
    }

    public async Task<ProviderMessage?> FetchMessageAsync(string providerMessageSid, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(token => _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: providerMessageSid,
                requestOptions: null,
                ct: token), ct);

            return ToProviderMessage(message);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider no longer holds this message (e.g. provider-side retention sweep).
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(nameof(FetchMessageAsync), ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(nameof(FetchMessageAsync), ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(nameof(FetchMessageAsync), ex);
        }
    }

    public async Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken ct)
    {
        try
        {
            await Bounded(async token =>
            {
                await _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid,
                    sid: providerMessageSid,
                    body: string.Empty,
                    status: null,
                    requestOptions: null,
                    ct: token);
            }, ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(nameof(RedactMessageBodyAsync), ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(nameof(RedactMessageBodyAsync), ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(nameof(RedactMessageBodyAsync), ex);
        }
    }

    public async Task<ProviderMessageList> ListMessagesFromSenderAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var messages = new List<ProviderMessage>();
        string? pageToken = null;
        var truncated = false;

        for (var page = 0; page < MaxReconciliationPages; page++)
        {
            ListMessageResponse response;
            try
            {
                var currentToken = pageToken;
                response = await Bounded(token => _client.Api20100401Message.ListMessage(
                    accountSid: _options.AccountSid,
                    to: null,
                    from: _options.FromNumber,
                    dateSent: null,
                    dateSentQuery: toUtc.ToUniversalTime(),
                    dateSentQueryQuery: fromUtc.ToUniversalTime(),
                    pageSize: ReconciliationPageSize,
                    page: null,
                    pageToken: currentToken,
                    requestOptions: null,
                    ct: token), ct);

                if (response.Messages is not null)
                {
                    messages.AddRange(response.Messages.Select(ToProviderMessage));
                }

                pageToken = ExtractPageToken(response.NextPageUri);
                if (pageToken is null || pageToken == currentToken)
                {
                    // End of range, or the provider failed to advance the cursor.
                    truncated = pageToken == currentToken && currentToken is not null;
                    if (truncated)
                    {
                        _logger.LogWarning("Reconciliation paging stopped: provider returned a non-advancing page token.");
                    }
                    return new ProviderMessageList(messages, truncated);
                }
            }
            catch (SdkException<RawError> ex)
            {
                throw Translate(nameof(ListMessagesFromSenderAsync), ex);
            }
            catch (JsonException ex)
            {
                throw Unprocessable(nameof(ListMessagesFromSenderAsync), ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw Unreachable(nameof(ListMessagesFromSenderAsync), ex);
            }
        }

        _logger.LogWarning("Reconciliation paging hit the {MaxPages}-page cap; the report is truncated.", MaxReconciliationPages);
        return new ProviderMessageList(messages, Truncated: true);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private static ProviderMessage ToProviderMessage(ApiV2010AccountMessage message) => new(
        message.Sid ?? string.Empty,
        message.Status?.Value,
        message.ErrorCode,
        message.ErrorMessage,
        message.To,
        message.From,
        message.Body,
        ParseProviderDate(message.DateSent));

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(nextPageUri[(queryIndex + 1)..]);
        return query.TryGetValue("PageToken", out var token) ? token.ToString() : null;
    }

    private static bool IsNumberRejection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity;

    private TwilioProviderException Translate(string operation, SdkException<RawError> ex)
    {
        int? providerCode = null;
        try
        {
            providerCode = ex.Error.ReadAsJson<TwilioErrorBody>()?.Code;
        }
        catch (JsonException)
        {
            // The provider error body was not JSON; the HTTP status alone carries the failure.
        }

        // Never log the raw provider body: it can embed the destination number.
        _logger.LogWarning(
            "Twilio {Operation} rejected the request: HTTP {StatusCode}, provider error code {ProviderCode}.",
            operation, (int)ex.Error.StatusCode, providerCode);

        return new TwilioProviderException(ex.Error.StatusCode, "The messaging provider rejected the request.", ex)
        {
            ProviderErrorCode = providerCode
        };
    }

    private TwilioProviderException Unprocessable(string operation, JsonException ex)
    {
        _logger.LogWarning("Twilio {Operation} returned a response that could not be processed.", operation);
        return new TwilioProviderException(null, "The messaging provider returned a response that could not be processed.", ex);
    }

    private TwilioProviderException Unreachable(string operation, Exception ex)
    {
        _logger.LogWarning("Twilio {Operation} could not reach the provider or exceeded its time budget.", operation);
        return new TwilioProviderException(null, "The messaging provider could not be reached.", ex);
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
