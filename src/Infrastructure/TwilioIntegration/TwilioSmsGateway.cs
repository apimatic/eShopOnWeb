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

namespace Microsoft.eShopWeb.Infrastructure.TwilioIntegration;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SmsLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "line_type_intelligence,line_status",
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

            var usable = lookup.Valid == true
                && string.IsNullOrWhiteSpace(lookup.PhoneNumber) == false
                && (lookup.ValidationErrors is null || lookup.ValidationErrors.Count == 0);

            return new SmsLookupResult(usable, lookup.PhoneNumber);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            if (status is 401 or 403)
            {
                throw new SmsProviderException("Provider unavailable.", ex.Error.StatusCode, ex);
            }

            if (status is >= 400 and < 500)
            {
                return new SmsLookupResult(false, null);
            }

            throw new SmsProviderException("Provider unavailable.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            var status = TwilioLastStatusHandler.LastStatus;
            if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                && status is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden)
            {
                return new SmsLookupResult(false, null);
            }

            throw new SmsProviderException("The provider returned a response that could not be processed.", status, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("Provider unavailable.", inner: ex);
        }
    }

    public Task<SmsSendResult> SendSmsAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateMessageSafe(to, body, scheduleType: null, sendAt: null, messagingServiceSid: null, cancellationToken);

    public Task<SmsSendResult> ScheduleSmsAsync(
        string to,
        string body,
        DateTimeOffset sendAt,
        CancellationToken cancellationToken) =>
        CreateMessageSafe(
            to,
            body,
            MessageEnumScheduleType.Fixed,
            sendAt,
            _settings.MessagingServiceSid,
            cancellationToken);

    public async Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnceHandler.BeginWrite())
            {
                var message = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: null,
                        status: MessageEnumUpdateStatus.Canceled,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                return ToSendResult(message, accepted: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Cancel scheduled message failed with {ExceptionType}", ex.GetType().Name);
            return new SmsSendResult(false, providerSid, null, null, "The provider could not cancel the scheduled message.");
        }
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken)
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

            return ToSnapshot(message);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Fetch message failed with {ExceptionType}", ex.GetType().Name);
            throw new SmsProviderException("Provider unavailable.", TwilioLastStatusHandler.LastStatus, ex);
        }
    }

    public async Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        SdkException<RawError>? notFound = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using (TwilioWriteOnceHandler.BeginWrite())
                {
                    await Bounded(
                        ct => _client.Api20100401Message.UpdateMessage(
                            accountSid: _settings.AccountSid,
                            sid: providerSid,
                            body: "",
                            status: null,
                            requestOptions: null,
                            ct: ct),
                        cancellationToken);
                }

                return;
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404 && attempt < 2)
            {
                notFound = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw MapSdkException(ex);
            }
            catch (JsonException ex)
            {
                throw new SmsProviderException("The provider returned a response that could not be processed.", TwilioLastStatusHandler.LastStatus, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TwilioDuplicateWriteException)
            {
                throw new SmsProviderException("Provider unavailable.", inner: ex);
            }
        }

        if (notFound is not null)
        {
            throw MapSdkException(notFound);
        }
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(25));
        var deadline = cts.Token;

        const int maxPages = 20;
        const long pageSize = 1000;
        var results = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        int? page = null;
        var pages = 0;

        try
        {
            while (pages < maxPages)
            {
                pages++;
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: pageSize,
                    page: page,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: deadline);

                if (response.Messages is not null)
                {
                    foreach (var message in response.Messages)
                    {
                        results.Add(ToSnapshot(message));
                    }
                }

                if (string.IsNullOrWhiteSpace(response.NextPageUri))
                {
                    break;
                }

                pageToken = ExtractPageToken(response.NextPageUri);
                page = (response.Page ?? 0) + 1;

                var pageCount = response.Messages?.Count ?? 0;
                if (pageCount == 0)
                {
                    break;
                }
            }

            if (pages >= maxPages)
            {
                _logger.LogWarning("Reconciliation listing stopped after {MaxPages} pages; the range may be incomplete.", maxPages);
            }

            return results;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapSdkException(ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", TwilioLastStatusHandler.LastStatus, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("Provider unavailable.", inner: ex);
        }
    }

    private async Task<SmsSendResult> CreateMessageSafe(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? messagingServiceSid,
        CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnceHandler.BeginWrite())
            {
                var message = await Bounded(
                    ct => _client.Api20100401Message.CreateMessage(
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
                        from: _settings.FromNumber,
                        fallbackFrom: null,
                        messagingServiceSid: messagingServiceSid,
                        body: body,
                        mediaUrl: null,
                        contentSid: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                return ToSendResult(message, accepted: true);
            }
        }
        catch (TwilioDuplicateWriteException)
        {
            _logger.LogWarning("Duplicate messaging write was refused.");
            return new SmsSendResult(false, null, null, null, "The send outcome is unknown.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Create message failed with {ExceptionType}", ex.GetType().Name);
            return new SmsSendResult(false, null, null, null, "The provider could not send the message.");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static SmsSendResult ToSendResult(ApiV2010AccountMessage message, bool accepted) =>
        new(accepted, message.Sid, StatusWire(message.Status), message.ErrorCode, message.ErrorMessage);

    private static SmsMessageSnapshot ToSnapshot(ApiV2010AccountMessage message) =>
        new(
            message.Sid,
            StatusWire(message.Status),
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            message.DateCreated,
            message.DateSent);

    private static string? StatusWire(MessageEnumStatus? status) => status?.Value;

    private static SmsProviderException MapSdkException(SdkException<RawError> ex)
    {
        var status = ex.Error.StatusCode;
        var code = (int)status;
        if (code is 401 or 403)
        {
            return new SmsProviderException("Provider unavailable.", status, ex);
        }

        if (code == 429)
        {
            return new SmsProviderException("Temporarily unavailable.", status, ex);
        }

        if (code is >= 400 and < 500)
        {
            return new SmsProviderException("The provider rejected the request.", status, ex);
        }

        return new SmsProviderException("Provider unavailable.", status, ex);
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        try
        {
            var uri = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new Uri(nextPageUri)
                : new Uri("https://api.twilio.com" + (nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri));

            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(part[..eq]);
                if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(part[(eq + 1)..]);
                }
            }
        }
        catch (UriFormatException)
        {
            return null;
        }

        return null;
    }
}
