using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 100;
    private const long ListPageSize = 1000;

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

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupNumberAsync(
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "validation,line_type_intelligence,line_status",
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
                    ct: ct),
                cancellationToken);

            var errors = response.ValidationErrors?
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToList() ?? new List<string>();

            var usable = response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber);
            return new PhoneNumberLookupResult(
                usable,
                response.PhoneNumber,
                response.NationalFormat,
                errors);
        }
        catch (SdkException<RawError> ex) when (IsCallerNumberRejection(ex.Error.StatusCode))
        {
            return new PhoneNumberLookupResult(false, null, null, Array.Empty<string>());
        }
        catch (SdkException<RawError> ex)
        {
            throw MapSdkException(ex, "The messaging provider rejected the number lookup.");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException(
                "The provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    public async Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken)
    {
        var scheduled = request.SendAt.HasValue;
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.CreateMessage(
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
                    scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
                    sendAt: request.SendAt,
                    sendAsMms: null,
                    contentVariables: null,
                    riskCheck: null,
                    from: _settings.FromNumber,
                    fallbackFrom: null,
                    messagingServiceSid: scheduled ? _settings.MessagingServiceSid : null,
                    body: request.Body,
                    mediaUrl: null,
                    contentSid: null,
                    ct: ct),
                cancellationToken);

            return MapMessage(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapSdkException(ex, "The messaging provider rejected the send request.");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException(
                "The provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is unreachable.", innerException: ex);
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
                    ct: ct),
                cancellationToken);
            return MapMessage(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapSdkException(ex, "The messaging provider could not return this message.");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException(
                "The provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    public async Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    ct: ct),
                cancellationToken);
            return MapMessage(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapSdkException(ex, "The messaging provider could not cancel the scheduled message.");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException(
                "The provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    public async Task<SmsMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: string.Empty,
                    status: null,
                    ct: ct),
                cancellationToken);
            return MapMessage(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapSdkException(ex, "The messaging provider could not dispose of the message content.");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException(
                "The provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        int? page = null;
        var pages = 0;

        try
        {
            while (true)
            {
                if (++pages > MaxListPages)
                {
                    _logger.LogWarning(
                        "Reconciliation listing stopped after {MaxPages} pages; remaining provider pages were not fetched.",
                        MaxListPages);
                    break;
                }

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
                        ct: ct),
                    cancellationToken);

                if (response.Messages is not null)
                {
                    results.AddRange(response.Messages.Select(MapMessage));
                }

                if (string.IsNullOrWhiteSpace(response.NextPageUri))
                {
                    break;
                }

                pageToken = ExtractPageToken(response.NextPageUri);
                page = pageToken is null ? (response.Page ?? 0) + 1 : null;
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw MapSdkException(ex, "The messaging provider could not list messages.");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException(
                "The provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is unreachable.", innerException: ex);
        }

        return results;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static SmsMessageSnapshot MapMessage(ApiV2010AccountMessage message) =>
        new(
            message.Sid,
            message.Status?.Value,
            message.To,
            message.From,
            message.Body,
            message.DateSent,
            message.DateCreated,
            message.ErrorCode,
            message.ErrorMessage,
            message.MessagingServiceSid);

    private static bool IsCallerNumberRejection(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status is >= 400 and < 500 && status is not 401 and not 403 and not 429;
    }

    private SmsGatewayException MapSdkException(SdkException<RawError> ex, string fallback)
    {
        var status = (int)ex.Error.StatusCode;
        _logger.LogWarning("Twilio API error HTTP {StatusCode}", status);

        var message = status switch
        {
            401 or 403 => "The messaging provider rejected our credentials.",
            429 => "The messaging provider is temporarily unavailable.",
            >= 400 and < 500 => fallback,
            _ => "The messaging provider is unavailable."
        };

        return new SmsGatewayException(message, status, ex);
    }

    private static string? ExtractPageToken(string nextPageUri)
    {
        var relative = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(nextPageUri)
            : new Uri(new Uri("https://api.twilio.com"), nextPageUri);

        var query = relative.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var part in query.Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }
}
