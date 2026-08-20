using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private const int MaxListPages = 25;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<LookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "validation",
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

            if (response.Valid == false || (response.ValidationErrors is { Count: > 0 }))
            {
                return new LookupResult.Unusable("The provider does not consider this a usable destination.");
            }

            if (string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                return new LookupResult.Unusable("The provider did not return a canonical destination number.");
            }

            return new LookupResult.Usable(response.PhoneNumber);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            _logger.LogWarning("Phone number lookup failed with provider status {StatusCode}.", status);
            if (status is 401 or 403)
            {
                throw new SmsProviderException("Provider unavailable.", ex.Error.StatusCode, ex);
            }

            if (status == 429)
            {
                throw new SmsProviderException("Temporarily unavailable.", ex.Error.StatusCode, ex);
            }

            if (status is >= 400 and < 500)
            {
                return new LookupResult.Unusable("The provider does not consider this a usable destination.");
            }

            throw new SmsProviderException("Provider unavailable.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The messaging provider could not be reached.", inner: ex);
        }
    }

    public Task<GatewayResult> SendImmediateAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateMessageAsync(to, body, scheduleType: null, sendAt: null, messagingServiceSid: null, cancellationToken);

    public Task<GatewayResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateMessageAsync(
            to,
            body,
            MessageEnumScheduleType.Fixed,
            sendAt,
            _settings.MessagingServiceSid,
            cancellationToken);

    public Task<GatewayResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken) =>
        UpdateAsync(providerSid, body: null, MessageEnumUpdateStatus.Canceled, cancellationToken);

    public async Task<GatewayResult> FetchAsync(string providerSid, CancellationToken cancellationToken)
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
            return new GatewayResult.Ok(Map(message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return TranslateWriteOrReadFailure(ex, "fetch");
        }
    }

    public Task<GatewayResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken) =>
        UpdateAsync(providerSid, body: string.Empty, status: null, cancellationToken);

    public async Task<ProviderMessageList> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var collected = new List<ProviderMessage>();
        int? page = null;
        string? pageToken = null;
        string? previousPageToken = null;
        int? previousPage = null;

        for (var pages = 0; pages < MaxListPages; pages++)
        {
            ListMessageResponse response;
            try
            {
                var currentPage = page;
                var currentToken = pageToken;
                response = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: to,
                        dateSentQueryQuery: from,
                        pageSize: 1000,
                        page: currentPage,
                        pageToken: currentToken,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SdkException<RawError> ex)
            {
                _logger.LogWarning("Reconciliation list failed with provider status {StatusCode}.", (int)ex.Error.StatusCode);
                throw new SmsProviderException("Provider unavailable.", ex.Error.StatusCode, ex);
            }
            catch (JsonException ex)
            {
                throw new SmsProviderException("The provider returned a response that could not be processed.", inner: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new SmsProviderException("The messaging provider could not be reached.", inner: ex);
            }

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    collected.Add(Map(message));
                }
            }

            if (string.IsNullOrWhiteSpace(response.NextPageUri))
            {
                return new ProviderMessageList(collected, Truncated: false);
            }

            (page, pageToken) = ParseNextPage(response.NextPageUri);
            if (page == previousPage && string.Equals(pageToken, previousPageToken, StringComparison.Ordinal))
            {
                _logger.LogWarning("Reconciliation paging stopped because the provider returned a next page that did not advance.");
                return new ProviderMessageList(collected, Truncated: true);
            }

            previousPage = page;
            previousPageToken = pageToken;
        }

        _logger.LogWarning("Reconciliation paging stopped after {MaxPages} pages.", MaxListPages);
        return new ProviderMessageList(collected, Truncated: true);
    }

    private async Task<GatewayResult> CreateMessageAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? messagingServiceSid,
        CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioAtMostOnceWriteHandler.BeginCreateMessageScope())
            {
                var created = await Bounded(
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
                return new GatewayResult.Ok(Map(created));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return TranslateWriteOrReadFailure(ex, "create");
        }
    }

    private async Task<GatewayResult> UpdateAsync(
        string providerSid,
        string? body,
        MessageEnumUpdateStatus? status,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: body,
                    status: status,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return new GatewayResult.Ok(Map(updated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return TranslateWriteOrReadFailure(ex, "update");
        }
    }

    private GatewayResult TranslateWriteOrReadFailure(Exception ex, string operation)
    {
        switch (ex)
        {
            case SdkException<RawError> sdk:
                _logger.LogWarning("Messaging {Operation} failed with provider status {StatusCode}.", operation, (int)sdk.Error.StatusCode);
                var status = (int)sdk.Error.StatusCode;
                if (status is 401 or 403)
                {
                    return new GatewayResult.Failed("Provider unavailable.", status);
                }

                if (status == 429)
                {
                    return new GatewayResult.Failed("Temporarily unavailable.", status);
                }

                if (status is >= 400 and < 500)
                {
                    return new GatewayResult.Failed("The provider rejected the message.", status);
                }

                return new GatewayResult.Failed("Provider unavailable.", status);
            case JsonException:
                _logger.LogWarning("Messaging {Operation} returned a response that could not be processed.", operation);
                return new GatewayResult.Unknown("The provider returned a response that could not be processed.");
            case TwilioDuplicateWriteException:
                _logger.LogWarning("Messaging {Operation} was not retried after a transport failure.", operation);
                return new GatewayResult.Unknown("The provider outcome is unknown because a duplicate send was blocked.");
            case HttpRequestException:
            case TaskCanceledException:
                _logger.LogWarning("Messaging {Operation} could not reach the provider.", operation);
                return new GatewayResult.Unknown("The messaging provider could not be reached.");
            default:
                _logger.LogWarning("Messaging {Operation} failed unexpectedly.", operation);
                return new GatewayResult.Unknown("The messaging provider could not be reached.");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message) =>
        new(
            message.Sid,
            message.Status?.Value ?? "unknown",
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            message.From,
            message.To,
            message.DateSent);

    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        var uri = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(nextPageUri)
            : new Uri(new Uri("https://api.twilio.com"), nextPageUri);

        int? page = null;
        string? pageToken = null;
        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query))
        {
            return (null, null);
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var parsedPage))
            {
                page = parsedPage;
            }
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                pageToken = value;
            }
        }

        return (page, pageToken);
    }
}
