using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public sealed class TwilioSmsGateway : ISmsGateway, IPhoneNumberLookup
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;
    private const long ListPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> options, ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: rawNumber,
                    fields: "validation,line_type_intelligence",
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

            if (lookup.Valid != true
                || (lookup.ValidationErrors is { Count: > 0 })
                || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
            {
                return PhoneLookupResult.NotUsable("The provider does not consider this a usable destination.");
            }

            return PhoneLookupResult.Usable(lookup.PhoneNumber);
        }
        catch (SdkException<RawError> ex)
        {
            return MapLookupError(ex);
        }
        catch (JsonException)
        {
            return PhoneLookupResult.ProviderFault("The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return PhoneLookupResult.ProviderFault("The messaging provider is unreachable.");
        }
    }

    public Task<SmsDispatchResult> SendAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateMessageAsync(to, body, scheduleType: null, sendAt: null, messagingServiceSid: null, cancellationToken);

    public Task<SmsDispatchResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateMessageAsync(
            to,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            messagingServiceSid: _settings.MessagingServiceSid,
            cancellationToken);

    public async Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        using var _ = TwilioWriteGuard.Begin();
        try
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
            return ToSnapshot(updated, succeeded: true);
        }
        catch (TwilioDuplicateWritePreventedException)
        {
            return await FetchAsync(providerSid, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Cancel message {SidPresent} failed with HTTP {Status}", !string.IsNullOrEmpty(providerSid), (int)ex.Error.StatusCode);
            var fetched = await TryFetch(providerSid, cancellationToken);
            if (fetched is not null)
            {
                return fetched;
            }

            return FailedSnapshot("The provider could not cancel the scheduled message.", ex.Error.StatusCode);
        }
        catch (JsonException)
        {
            return FailedSnapshot("The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FailedSnapshot("The messaging provider is unreachable.");
        }
    }

    public async Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken)
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
            return ToSnapshot(message, succeeded: true);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Fetch message failed with HTTP {Status}", (int)ex.Error.StatusCode);
            return FailedSnapshot("The provider could not return the message.", ex.Error.StatusCode);
        }
        catch (JsonException)
        {
            return FailedSnapshot("The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FailedSnapshot("The messaging provider is unreachable.");
        }
    }

    public async Task<SmsMessageSnapshot> RedactContentAsync(string providerSid, CancellationToken cancellationToken)
    {
        using var _ = TwilioWriteGuard.Begin();
        try
        {
            var updated = await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: string.Empty,
                    status: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return ToSnapshot(updated, succeeded: true);
        }
        catch (TwilioDuplicateWritePreventedException)
        {
            return await FetchAsync(providerSid, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Redact message failed with HTTP {Status}", (int)ex.Error.StatusCode);
            return FailedSnapshot("The provider could not dispose of the message content.", ex.Error.StatusCode);
        }
        catch (JsonException)
        {
            return FailedSnapshot("The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FailedSnapshot("The messaging provider is unreachable.");
        }
    }

    public async Task<SmsListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var collected = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        int? page = 0;
        var truncated = false;

        try
        {
            for (var pageCount = 0; pageCount < MaxListPages; pageCount++)
            {
                var response = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: to,
                        dateSentQueryQuery: from,
                        pageSize: ListPageSize,
                        page: page,
                        pageToken: pageToken,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                var messages = response.Messages ?? Array.Empty<ApiV2010AccountMessage>();
                foreach (var message in messages)
                {
                    collected.Add(ToSnapshot(message, succeeded: true));
                }

                if (string.IsNullOrEmpty(response.NextPageUri) || messages.Count == 0)
                {
                    return new SmsListResult(true, collected, Truncated: false, FailureMessage: null);
                }

                pageToken = TryReadQueryValue(response.NextPageUri, "PageToken");
                page = response.Page is int current ? current + 1 : page + 1;
            }

            truncated = true;
            _logger.LogWarning("Reconciliation list hit the page cap; results are truncated.");
            return new SmsListResult(true, collected, truncated, FailureMessage: null);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("List messages failed with HTTP {Status}", (int)ex.Error.StatusCode);
            return new SmsListResult(false, collected, truncated, "The provider could not list messages.");
        }
        catch (JsonException)
        {
            return new SmsListResult(false, collected, truncated, "The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new SmsListResult(false, collected, truncated, "The messaging provider is unreachable.");
        }
    }

    private async Task<SmsDispatchResult> CreateMessageAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? messagingServiceSid,
        CancellationToken cancellationToken)
    {
        using var _ = TwilioWriteGuard.Begin();
        try
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

            return new SmsDispatchResult(
                Accepted: true,
                ProviderSid: created.Sid,
                Status: created.Status?.Value,
                DateSent: created.DateSent,
                ErrorCode: created.ErrorCode,
                ErrorMessage: created.ErrorMessage);
        }
        catch (TwilioDuplicateWritePreventedException)
        {
            _logger.LogWarning("Twilio write retry was blocked; treating the send as an unknown outcome.");
            return new SmsDispatchResult(false, null, "unknown", null, null, "Send outcome could not be confirmed.");
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Create message failed with HTTP {Status}", (int)ex.Error.StatusCode);
            return new SmsDispatchResult(false, null, "failed", null, (int)ex.Error.StatusCode, "The provider did not accept the message.");
        }
        catch (JsonException)
        {
            return new SmsDispatchResult(false, null, "failed", null, null, "The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new SmsDispatchResult(false, null, "failed", null, null, "The messaging provider is unreachable.");
        }
    }

    private async Task<SmsMessageSnapshot?> TryFetch(string providerSid, CancellationToken cancellationToken)
    {
        var snapshot = await FetchAsync(providerSid, cancellationToken);
        return snapshot.Succeeded ? snapshot : null;
    }

    private static PhoneLookupResult MapLookupError(SdkException<RawError> ex)
    {
        var code = (int)ex.Error.StatusCode;
        if (code is 401 or 403)
        {
            return PhoneLookupResult.ProviderFault("The messaging provider rejected our credentials.");
        }

        if (code >= 400 && code < 500)
        {
            return PhoneLookupResult.NotUsable("The provider does not consider this a usable destination.");
        }

        return PhoneLookupResult.ProviderFault("The messaging provider is unavailable.");
    }

    private static SmsMessageSnapshot ToSnapshot(ApiV2010AccountMessage message, bool succeeded) =>
        new(
            Succeeded: succeeded,
            Sid: message.Sid,
            Status: message.Status?.Value,
            From: message.From,
            To: message.To,
            Body: message.Body,
            ErrorCode: message.ErrorCode,
            ErrorMessage: message.ErrorMessage,
            DateSent: message.DateSent,
            DateCreated: message.DateCreated,
            FailureMessage: null);

    private static SmsMessageSnapshot FailedSnapshot(string message, HttpStatusCode? status = null) =>
        new(false, null, status is null ? null : ((int)status).ToString(), null, null, null, (int?)status, message, null, null, message);

    private static string? TryReadQueryValue(string uri, string key)
    {
        var queryIndex = uri.IndexOf('?', StringComparison.Ordinal);
        var query = queryIndex >= 0 ? uri[(queryIndex + 1)..] : uri;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }
}
