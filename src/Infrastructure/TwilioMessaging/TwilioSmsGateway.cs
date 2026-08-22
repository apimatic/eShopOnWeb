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
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.TwilioMessaging;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                    ct: ct),
                cancellationToken);

            if (response.Valid == false
                || (response.ValidationErrors is { Count: > 0 }))
            {
                return new PhoneNumberLookup(false, response.PhoneNumber, DescribeValidation(response));
            }

            if (string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                return new PhoneNumberLookup(false, null, "The number is not a usable destination.");
            }

            if (response.Valid == true || response.Valid is null)
            {
                return new PhoneNumberLookup(true, response.PhoneNumber, null);
            }

            return new PhoneNumberLookup(false, response.PhoneNumber, "The number is not a usable destination.");
        }
        catch (SdkException<RawError> ex) when (IsCallerReject(ex.Error.StatusCode))
        {
            return new PhoneNumberLookup(false, null, "The number is not a usable destination.");
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsProviderException(
                "The messaging provider could not validate the number.",
                ex.Error.StatusCode,
                ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException(
                "The provider returned a response that could not be processed.",
                null,
                ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The messaging provider is unreachable.", null, ex);
        }
    }

    public Task<SmsSendAttempt> SendImmediateAsync(string toCanonical, string body, CancellationToken cancellationToken) =>
        CreateAsync(toCanonical, body, scheduleType: null, sendAt: null, useMessagingService: false, cancellationToken);

    public Task<SmsSendAttempt> ScheduleAsync(
        string toCanonical,
        string body,
        DateTimeOffset sendAtUtc,
        CancellationToken cancellationToken) =>
        CreateAsync(
            toCanonical,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAtUtc.ToUniversalTime(),
            useMessagingService: true,
            cancellationToken);

    public async Task<SmsSendAttempt> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnce.Enter())
            {
                var updated = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: null,
                        status: MessageEnumUpdateStatus.Canceled,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                return FromMessage(updated, accepted: true);
            }
        }
        catch (Exception ex)
        {
            return FromCaughtWrite(ex);
        }
    }

    public async Task<SmsSendAttempt> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnce.Enter())
            {
                var updated = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: "",
                        status: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                return FromMessage(updated, accepted: true);
            }
        }
        catch (Exception ex)
        {
            return FromCaughtWrite(ex);
        }
    }

    public async Task<ProviderMessage?> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return ToProviderMessage(message);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsProviderException(
                "The messaging provider could not return the message.",
                ex.Error.StatusCode,
                ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException(
                "The provider returned a response that could not be processed.",
                null,
                ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The messaging provider is unreachable.", null, ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var results = new List<ProviderMessage>();
        string? pageToken = null;
        var pages = 0;
        var truncated = false;

        try
        {
            do
            {
                var page = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: toUtc.ToUniversalTime(),
                        dateSentQueryQuery: fromUtc.ToUniversalTime(),
                        pageSize: 1000,
                        page: null,
                        pageToken: pageToken,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                if (page.Messages is not null)
                {
                    foreach (var message in page.Messages)
                    {
                        var mapped = ToProviderMessage(message);
                        if (mapped is not null)
                        {
                            results.Add(mapped);
                        }
                    }
                }

                pageToken = ExtractPageToken(page.NextPageUri);
                pages++;
                if (pageToken is not null && pages >= MaxListPages)
                {
                    truncated = true;
                    break;
                }
            } while (pageToken is not null);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsProviderException(
                "The messaging provider could not list messages.",
                ex.Error.StatusCode,
                ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException(
                "The provider returned a response that could not be processed.",
                null,
                ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The messaging provider is unreachable.", null, ex);
        }

        if (truncated)
        {
            _logger.LogWarning("Reconciliation listing stopped after {PageCount} pages.", pages);
        }

        return results;
    }

    private async Task<SmsSendAttempt> CreateAsync(
        string toCanonical,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        bool useMessagingService,
        CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnce.Enter())
            {
                var created = await Bounded(
                    ct => _client.Api20100401Message.CreateMessage(
                        accountSid: _settings.AccountSid,
                        to: toCanonical,
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
                        from: _settings.FromNumber,
                        fallbackFrom: null,
                        messagingServiceSid: useMessagingService ? _settings.MessagingServiceSid : null,
                        body: body,
                        mediaUrl: null,
                        contentSid: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                return FromMessage(created, accepted: true);
            }
        }
        catch (Exception ex)
        {
            return FromCaughtWrite(ex);
        }
    }

    private SmsSendAttempt FromCaughtWrite(Exception ex)
    {
        switch (ex)
        {
            case TwilioDuplicateWriteException:
                _logger.LogWarning("Blocked a duplicate provider write.");
                return new SmsSendAttempt(false, null, "unknown", null, "Duplicate write blocked after a transport retry.", true);
            case SdkException<RawError> sdk:
                _logger.LogWarning("Provider write returned HTTP {StatusCode}.", (int)sdk.Error.StatusCode);
                return new SmsSendAttempt(
                    false,
                    null,
                    "send_failed",
                    TryReadErrorCode(sdk.Error),
                    TryReadErrorMessage(sdk.Error),
                    false);
            case JsonException:
                _logger.LogWarning("Provider write returned an unreadable response.");
                return new SmsSendAttempt(false, null, "unknown", null, "The provider returned a response that could not be processed.", true);
            case HttpRequestException:
            case TaskCanceledException:
                _logger.LogWarning("Provider write did not complete.");
                return new SmsSendAttempt(false, null, "unknown", null, "The messaging provider is unreachable.", true);
            default:
                _logger.LogWarning("Provider write failed.");
                return new SmsSendAttempt(false, null, "unknown", null, "The messaging provider is unreachable.", true);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static SmsSendAttempt FromMessage(ApiV2010AccountMessage message, bool accepted) =>
        new(
            accepted,
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            false);

    private static ProviderMessage? ToProviderMessage(ApiV2010AccountMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Sid))
        {
            return null;
        }

        return new ProviderMessage(
            message.Sid,
            message.Status?.Value,
            message.Body,
            message.From,
            message.To,
            message.ErrorCode,
            message.ErrorMessage,
            message.DateSent,
            message.DateCreated,
            message.DateUpdated);
    }

    private static bool IsCallerReject(HttpStatusCode statusCode) =>
        (int)statusCode is >= 400 and < 500 && statusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden;

    private static string DescribeValidation(LookupResponse response)
    {
        if (response.ValidationErrors is { Count: > 0 })
        {
            return "The number is not a usable destination.";
        }

        return "The number is not a usable destination.";
    }

    private static int? TryReadErrorCode(RawError error)
    {
        try
        {
            using var document = JsonDocument.Parse(error.ReadAsString());
            if (document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string TryReadErrorMessage(RawError error)
    {
        try
        {
            using var document = JsonDocument.Parse(error.ReadAsString());
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? $"HTTP {(int)error.StatusCode}";
            }
        }
        catch (JsonException)
        {
        }

        var raw = error.ReadAsString();
        return string.IsNullOrWhiteSpace(raw) ? $"HTTP {(int)error.StatusCode}" : "The messaging provider rejected the request.";
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0 || queryIndex == nextPageUri.Length - 1)
        {
            return null;
        }

        var query = nextPageUri[(queryIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (!string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[1]);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
