using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingGateway : IMessagingGateway
{
    private const int MaxListPages = 50;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(90);

    private readonly TwilioClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingGateway> _logger;

    public TwilioMessagingGateway(
        TwilioClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioMessagingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<PhoneLookupResult> LookupNumberAsync(string phoneNumber, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(
                token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                    ct: token),
                ct);

            if (response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                return new PhoneLookupResult(true, response.PhoneNumber, null);
            }

            var reason = response.ValidationErrors is { Count: > 0 }
                ? string.Join(", ", response.ValidationErrors.Select(e => e.Value))
                : "The provider does not consider this number a usable destination.";
            return new PhoneLookupResult(false, null, reason);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode is >= 400 and < 500)
        {
            _logger.LogWarning("Phone lookup rejected by provider with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            return new PhoneLookupResult(false, null, "The provider does not consider this number a usable destination.");
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw new MessagingGatewayException("The provider could not complete the number lookup.", ex);
        }
    }

    public async Task<ProviderMessageSnapshot> SendAsync(SendMessageRequest request, CancellationToken ct)
    {
        try
        {
            ApiV2010AccountMessage created = await Bounded(
                token => _client.Api20100401Message.CreateMessage(
                    accountSid: _settings.AccountSid,
                    to: request.To,
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
                    scheduleType: request.ScheduleForLater ? MessageEnumScheduleType.Fixed : null,
                    sendAt: request.ScheduleForLater ? request.SendAt : null,
                    sendAsMms: null,
                    contentVariables: null,
                    riskCheck: null,
                    from: _settings.FromNumber,
                    fallbackFrom: null,
                    messagingServiceSid: request.ScheduleForLater ? _settings.MessagingServiceSid : null,
                    body: request.Body,
                    mediaUrl: null,
                    contentSid: null,
                    ct: token),
                ct);

            return ToSnapshot(created);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("CreateMessage failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            return new ProviderMessageSnapshot(
                null,
                "send_failed",
                null,
                null,
                null,
                (int)ex.Error.StatusCode,
                SafeErrorDetail(ex.Error),
                null,
                null);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "CreateMessage did not complete; outcome is unknown.");
            return new ProviderMessageSnapshot(
                null,
                "send_failed",
                null,
                null,
                null,
                null,
                "The provider could not be reached or returned an unreadable response.",
                null,
                null);
        }
    }

    public async Task<ProviderMessageSnapshot?> FetchAsync(string providerSid, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(
                token => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    ct: token),
                ct);
            return ToSnapshot(message);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw new MessagingGatewayException("The provider could not fetch the message.", ex);
        }
    }

    public async Task<ProviderMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(
                token => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: string.Empty,
                    status: null,
                    ct: token),
                ct);
            return ToSnapshot(message);
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw new MessagingGatewayException("The provider could not redact the message body.", ex);
        }
    }

    public async Task<ProviderMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(
                token => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    ct: token),
                ct);
            return ToSnapshot(message);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode is >= 400 and < 500)
        {
            _logger.LogWarning("Cancel scheduled message rejected with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw new MessagingGatewayException("The provider could not cancel the scheduled message.", ex);
        }
    }

    public async Task<ProviderMessageList> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var collected = new List<ProviderMessageSnapshot>();
        string? pageToken = null;
        int page = 0;
        bool truncated = false;

        for (var pageCount = 0; pageCount < MaxListPages; pageCount++)
        {
            ListMessageResponse response;
            try
            {
                var currentToken = pageToken;
                var currentPage = page;
                response = await Bounded(
                    token => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: to,
                        dateSentQueryQuery: from,
                        pageSize: 1000,
                        page: currentPage,
                        pageToken: currentToken,
                        ct: token),
                    ct);
            }
            catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
            {
                throw new MessagingGatewayException("The provider could not list messages for reconciliation.", ex);
            }

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    collected.Add(ToSnapshot(message));
                }
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                return new ProviderMessageList(collected, truncated);
            }

            pageToken = TryReadQueryValue(response.NextPageUri, "PageToken");
            page = (response.Page ?? page) + 1;
        }

        truncated = true;
        _logger.LogWarning("Reconciliation list stopped after {MaxPages} pages; the range may be incomplete.", MaxListPages);
        return new ProviderMessageList(collected, truncated);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static ProviderMessageSnapshot ToSnapshot(ApiV2010AccountMessage message) =>
        new(
            message.Sid,
            message.Status?.Value ?? "unknown",
            message.Body,
            message.To,
            message.From,
            message.ErrorCode,
            message.ErrorMessage,
            message.DateCreated,
            message.DateSent);

    private static string SafeErrorDetail(RawError error)
    {
        var raw = error.ReadAsString();
        if (string.IsNullOrEmpty(raw))
        {
            return $"HTTP {(int)error.StatusCode}";
        }

        return raw.Length <= 200 ? raw : raw[..200];
    }

    private static string? TryReadQueryValue(string uri, string key)
    {
        var queryIndex = uri.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0 || queryIndex == uri.Length - 1)
        {
            return null;
        }

        var query = uri[(queryIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(part[..eq]);
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[(eq + 1)..]);
            }
        }

        return null;
    }
}

public sealed class MessagingGatewayException : Exception
{
    public MessagingGatewayException(string message, Exception inner) : base(message, inner)
    {
    }
}
