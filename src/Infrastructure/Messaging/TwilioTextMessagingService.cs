using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio-backed text messaging. All calls are bounded by a single per-call budget and all
/// provider failures surface as <see cref="TextMessagingException"/> (or
/// <see cref="InvalidPhoneNumberException"/> for unusable destinations). Destination numbers
/// and credentials are never logged.
/// </summary>
public class TwilioTextMessagingService : ITextMessagingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioTextMessagingService> _logger;

    public TwilioTextMessagingService(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioTextMessagingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
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
                requestOptions: null,
                ct: c), ct);

            if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
            {
                return new ValidatedPhoneNumber(response.PhoneNumber!, response.NationalFormat);
            }

            throw new InvalidPhoneNumberException("The messaging provider does not consider this number a usable destination.");
        }
        catch (SdkException<RawError> ex) when (IsClientError(ex))
        {
            // The provider rejected the lookup itself — the number is not a usable destination.
            throw new InvalidPhoneNumberException("The messaging provider does not consider this number a usable destination.");
        }
        catch (Exception ex) when (ex is not InvalidPhoneNumberException)
        {
            throw Translate(ex, ct);
        }
    }

    public async Task<TextMessageResult> SendMessageAsync(string to, string body, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.CreateMessage(
                _settings.AccountSid,
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
                from: _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: c), ct);

            return ToResult(message);
        }
        catch (Exception ex) when (ex is not TextMessagingException)
        {
            throw Translate(ex, ct);
        }
    }

    public async Task<TextMessageResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        try
        {
            // Scheduled sends are Messaging-Services-only, so this uses MessagingServiceSid, not FromNumber.
            var message = await Bounded(c => _client.Api20100401Message.CreateMessage(
                _settings.AccountSid,
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
                requestOptions: null,
                ct: c), ct);

            return ToResult(message);
        }
        catch (Exception ex) when (ex is not TextMessagingException)
        {
            throw Translate(ex, ct);
        }
    }

    public async Task<TextMessageResult> CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.UpdateMessage(
                _settings.AccountSid,
                messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: c), ct);

            return ToResult(message);
        }
        catch (Exception ex) when (ex is not TextMessagingException)
        {
            throw Translate(ex, ct);
        }
    }

    public async Task<TextMessageResult> GetMessageAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(c => _client.Api20100401Message.FetchMessage(
                _settings.AccountSid,
                messageSid,
                requestOptions: null,
                ct: c), ct);

            return ToResult(message);
        }
        catch (Exception ex) when (ex is not TextMessagingException)
        {
            throw Translate(ex, ct);
        }
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            // Empty string sends Body= and erases the stored text; null would skip the parameter.
            await Bounded(c => _client.Api20100401Message.UpdateMessage(
                _settings.AccountSid,
                messageSid,
                body: "",
                status: null,
                requestOptions: null,
                ct: c), ct);
        }
        catch (Exception ex) when (ex is not TextMessagingException)
        {
            throw Translate(ex, ct);
        }
    }

    public async Task<IReadOnlyList<ProviderTextMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var messages = new List<ProviderTextMessage>();
        int? page = null;
        string? pageToken = null;
        var pageCount = 0;

        while (true)
        {
            ListMessageResponse response;
            try
            {
                // dateSentQuery -> "DateSent<" (before), dateSentQueryQuery -> "DateSent>" (after).
                response = await Bounded(c => _client.Api20100401Message.ListMessage(
                    _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: c), ct);
            }
            catch (Exception ex) when (ex is not TextMessagingException)
            {
                throw Translate(ex, ct);
            }

            if (response.Messages is not null)
            {
                messages.AddRange(response.Messages.Select(ToProviderMessage));
            }

            pageCount++;
            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }
            if (pageCount >= MaxReconciliationPages)
            {
                _logger.LogWarning("Reconciliation listing hit the {MaxPages}-page cap; the report is truncated.", MaxReconciliationPages);
                break;
            }

            (page, pageToken) = ParseNextPage(response.NextPageUri!);
            if (pageToken is null)
            {
                // No progress possible — stop rather than refetch the same page forever.
                _logger.LogWarning("Reconciliation listing stopped: provider returned a next page without a page token.");
                break;
            }
        }

        return messages;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static bool IsClientError(SdkException<RawError> ex)
        => (int)ex.Error.StatusCode >= 400 && (int)ex.Error.StatusCode < 500;

    private static TextMessageResult ToResult(ApiV2010AccountMessage message)
        => new(
            message.Sid ?? string.Empty,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            ParseWireDate(message.DateSent));

    private static ProviderTextMessage ToProviderMessage(ApiV2010AccountMessage message)
        => new(
            message.Sid ?? string.Empty,
            message.From,
            message.To,
            message.Status?.Value,
            ParseWireDate(message.DateSent),
            ParseWireDate(message.DateCreated),
            message.ErrorCode);

    private static DateTimeOffset? ParseWireDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0)
        {
            return (null, null);
        }

        int? page = null;
        string? pageToken = null;
        foreach (var pair in nextPageUri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(parts[0]);
            var value = Uri.UnescapeDataString(parts[1]);
            if (name.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
            }
            else if (name.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPage))
            {
                page = parsedPage;
            }
        }

        return (page, pageToken);
    }

    /// <summary>
    /// One ladder for every provider failure: API rejections carry their status and Twilio error
    /// code; transport failures and unreadable bodies become status-less TextMessagingExceptions.
    /// </summary>
    private static Exception Translate(Exception ex, CancellationToken ct)
    {
        switch (ex)
        {
            case SdkException<RawError> sdkEx:
                TwilioErrorDto? errorBody = null;
                try
                {
                    errorBody = sdkEx.Error.ReadAsJson<TwilioErrorDto>();
                }
                catch (JsonException)
                {
                    // Error body was not the expected JSON shape; the status still identifies the rejection.
                }

                var detail = errorBody?.Message ?? "The messaging provider rejected the request.";
                return new TextMessagingException(detail, sdkEx.Error.StatusCode, errorBody?.Code, sdkEx);

            case JsonException jsonEx:
                return new TextMessagingException("The messaging provider returned a response that could not be processed.", null, null, jsonEx);

            case Exception when (ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested:
                return new TextMessagingException("The messaging provider is unreachable or timed out.", null, null, ex);

            default:
                return ex;
        }
    }
}
