using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// INotificationGateway over the Twilio .NET SDK. Every SDK/transport/parse failure is
/// converted to <see cref="NotificationProviderException"/> here, so callers have exactly
/// one failure type. Phone numbers and message bodies are never written to logs.
/// </summary>
public class TwilioNotificationGateway : INotificationGateway
{
    public const string HttpClientName = "Twilio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;
    private const long ListPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioNotificationGateway> _logger;

    public TwilioNotificationGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings,
        IAppLogger<TwilioNotificationGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var response = await Bounded(c => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: c), ct);

            if (response.Valid == true)
            {
                return new PhoneNumberValidation(PhoneNumberValidity.Valid,
                    response.PhoneNumber, Array.Empty<string>());
            }

            var errors = response.ValidationErrors?
                .Select(e => e.Value ?? "invalid")
                .ToArray() ?? Array.Empty<string>();
            return new PhoneNumberValidation(PhoneNumberValidity.Invalid, null, errors);
        }
        catch (SdkException<RawError> ex)
        {
            // Lookup v2 unavailable on this account/number — fall back to v1.
            _logger.LogInformation($"Lookup v2 did not answer (HTTP {(int)ex.Error.StatusCode}); trying Lookup v1.");
            return await ValidateWithLookupV1Async(phoneNumber, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("Phone number validation could not reach the provider; number stored unverified.");
            return new PhoneNumberValidation(PhoneNumberValidity.Unverifiable, null, Array.Empty<string>());
        }
    }

    private async Task<PhoneNumberValidation> ValidateWithLookupV1Async(string phoneNumber, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.LookupsV1PhoneNumberApi.FetchPhoneNumber2(
                phoneNumber: phoneNumber,
                countryCode: null,
                type: null,
                addOns: null,
                addOnsData: null,
                ct: c), ct);

            // v1 has no Valid flag: a successful response means the number resolved.
            return new PhoneNumberValidation(PhoneNumberValidity.Valid,
                response.PhoneNumber, Array.Empty<string>());
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidation(PhoneNumberValidity.Invalid, null,
                new[] { "The provider does not consider this a usable phone number." });
        }
        catch (Exception ex) when (ex is SdkException<RawError> or HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("Phone number validation is unavailable on this account; number stored unverified.");
            return new PhoneNumberValidation(PhoneNumberValidity.Unverifiable, null, Array.Empty<string>());
        }
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct = default)
    {
        return CreateMessageAsync(to, body, from: _settings.FromNumber,
            messagingServiceSid: null, scheduleType: null, sendAt: null, ct);
    }

    public Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        // Provider-side scheduling is Messaging-Service-only, so this path sends via the
        // configured MessagingServiceSid instead of the From number.
        return CreateMessageAsync(to, body, from: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, ct);
    }

    private async Task<ProviderMessage> CreateMessageAsync(string to, string body, string? from,
        string? messagingServiceSid, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt,
        CancellationToken ct)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
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
                scheduleType: scheduleType,
                sendAt: sendAt,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: from,
                fallbackFrom: null,
                messagingServiceSid: messagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: c), ct);

            return ToProviderMessage(message);
        }
        catch (Exception ex) when (ex is not NotificationProviderException)
        {
            throw Convert(ex, "send a message");
        }
    }

    public async Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                ct: c), ct);

            return ToProviderMessage(message);
        }
        catch (Exception ex) when (ex is not NotificationProviderException)
        {
            throw Convert(ex, "fetch a message");
        }
    }

    public async Task CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            await Bounded(c => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: c), ct);
        }
        catch (Exception ex) when (ex is not NotificationProviderException)
        {
            throw Convert(ex, "cancel a scheduled message");
        }
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            await Bounded(c => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: "",
                status: null,
                ct: c), ct);
        }
        catch (Exception ex) when (ex is not NotificationProviderException)
        {
            throw Convert(ex, "redact a message body");
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        // The provider's date filters are date-granular; the SDK emits full UTC timestamps,
        // so normalize to UTC midnights and make the upper bound inclusive of the end day.
        // Wire names are inverted: dateSentQuery -> "DateSent<" (range END),
        // dateSentQueryQuery -> "DateSent>" (range START).
        var rangeStart = new DateTimeOffset(fromUtc.UtcDateTime.Date, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(toUtc.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

        var results = new List<ProviderMessage>();
        int? page = null;
        string? pageToken = null;
        var pages = 0;

        while (true)
        {
            TwilioSdk.Models.ListMessageResponse response;
            try
            {
                response = await Bounded(c => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: rangeEnd,
                    dateSentQueryQuery: rangeStart,
                    pageSize: ListPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: c), ct);
            }
            catch (Exception ex) when (ex is not NotificationProviderException)
            {
                throw Convert(ex, "list messages");
            }

            if (response.Messages is { Count: > 0 })
            {
                results.AddRange(response.Messages.Select(ToProviderMessage));
            }

            pages++;
            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }
            if (pages >= MaxListPages)
            {
                _logger.LogWarning($"Reconciliation listing hit the page cap ({MaxListPages}); the range may be truncated.");
                break;
            }

            (page, pageToken) = ParseNextPage(response.NextPageUri!);
            if (page is null && pageToken is null)
            {
                break;
            }
        }

        return results;
    }

    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        // next_page_uri carries the paging state as query params (Page / PageToken).
        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0)
        {
            return (null, null);
        }

        int? page = null;
        string? pageToken = null;
        foreach (var pair in nextPageUri[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }
            var name = Uri.UnescapeDataString(parts[0]);
            var value = Uri.UnescapeDataString(parts[1]);
            if (string.Equals(name, "Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
            {
                page = p;
            }
            else if (string.Equals(name, "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
            }
        }
        return (page, pageToken);
    }

    private static ProviderMessage ToProviderMessage(TwilioSdk.Models.ApiV2010AccountMessage message)
    {
        DateTimeOffset? dateSent = null;
        if (DateTimeOffset.TryParse(message.DateSent, out var parsed))
        {
            dateSent = parsed;
        }

        return new ProviderMessage(
            message.Sid ?? string.Empty,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.From,
            message.To,
            dateSent);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private NotificationProviderException Convert(Exception ex, string operation)
    {
        switch (ex)
        {
            case SdkException<RawError> sdkEx:
                var providerCode = TryReadProviderErrorCode(sdkEx.Error);
                _logger.LogWarning($"Twilio could not {operation}: HTTP {(int)sdkEx.Error.StatusCode}" +
                    (providerCode is null ? "" : $" (provider error {providerCode})") + ".");
                return new NotificationProviderException(
                    $"The messaging provider could not {operation} (HTTP {(int)sdkEx.Error.StatusCode}).",
                    sdkEx.Error.StatusCode, providerCode, sdkEx);

            case HttpRequestException or TaskCanceledException:
                _logger.LogWarning($"Twilio could not {operation}: the provider was unreachable or the call timed out.");
                return new NotificationProviderException(
                    "The messaging provider was unreachable.", null, null, ex);

            case JsonException:
                _logger.LogWarning($"Twilio could not {operation}: the provider response could not be processed.");
                return new NotificationProviderException(
                    "The messaging provider returned a response that could not be processed.", null, null, ex);

            default:
                _logger.LogWarning($"Twilio could not {operation}: unexpected failure ({ex.GetType().Name}).");
                return new NotificationProviderException(
                    "The messaging provider call failed unexpectedly.", null, null, ex);
        }
    }

    private static int? TryReadProviderErrorCode(RawError raw)
    {
        try
        {
            var body = raw.ReadAsJson<TwilioErrorBody>();
            return body?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
