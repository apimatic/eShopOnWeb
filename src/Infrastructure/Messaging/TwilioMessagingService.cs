using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of the messaging boundary. Every provider and transport failure
/// is converted to an outcome object here; nothing throws into the order pipeline.
/// Shopper phone numbers are never written to logs.
/// </summary>
public class TwilioMessagingService : IMessagingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxPages = 100;
    private const long PageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingService> _logger;

    public TwilioMessagingService(TwilioSdkClient client, IOptions<TwilioSettings> settings,
        IAppLogger<TwilioMessagingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<NumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var response = await Bounded(c => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: c), ct);

            return new NumberValidationResult
            {
                IsValid = response.Valid == true,
                CanonicalNumber = response.PhoneNumber,
                NationalFormat = response.NationalFormat,
                ValidationErrors = response.ValidationErrors?
                    .Select(e => e.Value)
                    .ToList() ?? (IReadOnlyList<string>)new List<string>()
            };
        }
        catch (SdkException<RawError> ex)
        {
            // A 4xx here is a definitive verdict on the number, not an outage.
            var (code, _) = ReadProviderError(ex);
            _logger.LogWarning("Number validation rejected by provider: HTTP {Status}, provider code {Code}.",
                (int)ex.Error.StatusCode, code?.ToString() ?? "none");
            return new NumberValidationResult
            {
                FailureKind = MessagingFailureKind.Rejected,
                IsValid = false,
                ProviderStatusCode = (int)ex.Error.StatusCode
            };
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            _logger.LogWarning("Number validation could not reach the provider: {Reason}.", ex.GetType().Name);
            return new NumberValidationResult { FailureKind = MessagingFailureKind.Unreachable };
        }
        catch (JsonException)
        {
            _logger.LogWarning("Number validation returned a response that could not be processed.");
            return new NumberValidationResult { FailureKind = MessagingFailureKind.UnprocessableResponse };
        }
    }

    public Task<MessagingOutcome> SendMessageAsync(string to, string body, CancellationToken ct = default) =>
        CreateMessageAsync(to, body, scheduled: null, ct);

    public Task<MessagingOutcome> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default) =>
        CreateMessageAsync(to, body, sendAt, ct);

    private async Task<MessagingOutcome> CreateMessageAsync(string to, string body, DateTimeOffset? scheduled, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.CreateMessage(
                _settings.AccountSid,
                to,
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
                // Scheduled messages go through the messaging service (provider requirement);
                // pass exactly one sender identity per call.
                scheduleType: scheduled.HasValue ? MessageEnumScheduleType.Fixed : null,
                sendAt: scheduled,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: scheduled.HasValue ? null : _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: scheduled.HasValue ? _settings.MessagingServiceSid : null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: c), ct);

            return MessagingOutcome.Succeeded(message.Sid, message.Status?.Value);
        }
        catch (Exception ex)
        {
            return ToFailure(ex, "send");
        }
    }

    public async Task<MessagingOutcome> CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.UpdateMessage(
                _settings.AccountSid,
                messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: c), ct);

            return MessagingOutcome.Succeeded(message.Sid, message.Status?.Value);
        }
        catch (Exception ex)
        {
            return ToFailure(ex, "cancel");
        }
    }

    public async Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.FetchMessage(
                _settings.AccountSid,
                messageSid,
                ct: c), ct);

            return Map(message);
        }
        catch (Exception ex) when (ex is SdkException<RawError> || IsTransportFailure(ex) || ex is JsonException)
        {
            _logger.LogWarning("Could not read provider state for message {MessageSid}: {Reason}.", messageSid, ex.GetType().Name);
            return null;
        }
    }

    public async Task<ListMessagesOutcome> ListMessagesAsync(DateTimeOffset sentAfter, DateTimeOffset sentBefore, CancellationToken ct = default)
    {
        var messages = new List<ProviderMessage>();
        try
        {
            string? pageToken = null;
            var pages = 0;
            do
            {
                var response = await Bounded(c => _client.Api20100401Message.ListMessage(
                    _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: sentBefore,
                    dateSentQueryQuery: sentAfter,
                    pageSize: PageSize,
                    page: null,
                    pageToken: pageToken,
                    ct: c), ct);

                if (response.Messages is not null)
                {
                    messages.AddRange(response.Messages.Select(Map));
                }

                pageToken = ExtractPageToken(response.NextPageUri);
                pages++;
            }
            while (pageToken is not null && pages < MaxPages);

            return new ListMessagesOutcome
            {
                Messages = messages,
                Truncated = pageToken is not null
            };
        }
        catch (Exception ex) when (ex is SdkException<RawError> || IsTransportFailure(ex) || ex is JsonException)
        {
            var kind = ex is SdkException<RawError> sdkEx
                ? MessagingFailureKind.Rejected
                : ex is JsonException ? MessagingFailureKind.UnprocessableResponse : MessagingFailureKind.Unreachable;
            int? status = (ex as SdkException<RawError>)?.Error.StatusCode is { } sc ? (int)sc : null;
            _logger.LogWarning("Listing provider messages failed ({Kind}, HTTP {Status}).", kind, status?.ToString() ?? "none");
            return new ListMessagesOutcome { FailureKind = kind, ProviderStatusCode = status };
        }
    }

    public async Task<MessagingOutcome> RedactMessageBodyAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            // Empty string is transmitted (only null is skipped by the SDK); the provider
            // erases the body while keeping the message record and its delivery outcome.
            var message = await Bounded(c => _client.Api20100401Message.UpdateMessage(
                _settings.AccountSid,
                messageSid,
                body: "",
                status: null,
                ct: c), ct);

            return MessagingOutcome.Succeeded(message.Sid, message.Status?.Value);
        }
        catch (Exception ex)
        {
            return ToFailure(ex, "redact");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private MessagingOutcome ToFailure(Exception ex, string operation)
    {
        switch (ex)
        {
            case SdkException<RawError> sdkEx:
                var (code, message) = ReadProviderError(sdkEx);
                _logger.LogWarning("Provider rejected message {Operation}: HTTP {Status}, provider code {Code}.",
                    operation, (int)sdkEx.Error.StatusCode, code?.ToString() ?? "none");
                return MessagingOutcome.Failed(MessagingFailureKind.Rejected,
                    (int)sdkEx.Error.StatusCode, code, message);
            case JsonException:
                _logger.LogWarning("Provider returned an unprocessable response during message {Operation}.", operation);
                return MessagingOutcome.Failed(MessagingFailureKind.UnprocessableResponse);
            default:
                _logger.LogWarning("Provider unreachable during message {Operation}: {Reason}.", operation, ex.GetType().Name);
                return MessagingOutcome.Failed(MessagingFailureKind.Unreachable);
        }
    }

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static (int? Code, string? Message) ReadProviderError(SdkException<RawError> ex)
    {
        try
        {
            var dto = ex.Error.ReadAsJson<TwilioErrorDto>();
            return (dto?.Code, dto?.Message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static ProviderMessage Map(TwilioSdk.Models.ApiV2010AccountMessage message) =>
        new()
        {
            Sid = message.Sid ?? string.Empty,
            To = message.To,
            From = message.From,
            Status = message.Status?.Value,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            DateSent = ParseProviderDate(message.DateSent),
            Body = message.Body
        };

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

        // Tolerate both absolute and relative next_page_uri values.
        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        foreach (var pair in nextPageUri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null;
    }

    /// <summary>Twilio's error JSON shape: code / message / more_info / status.</summary>
    private sealed class TwilioErrorDto
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("more_info")]
        public string? MoreInfo { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }
    }
}
