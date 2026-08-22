using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
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

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public sealed class TwilioSmsNotificationGateway : ISmsNotificationGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 20;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsNotificationGateway> _logger;

    public TwilioSmsNotificationGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioSmsNotificationGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupDestinationAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "line_type_intelligence",
                    countryCode: string.IsNullOrWhiteSpace(countryCode) ? null : countryCode,
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

            var usable = response.Valid == true
                && string.IsNullOrWhiteSpace(response.PhoneNumber) == false
                && (response.ValidationErrors is null || response.ValidationErrors.Count == 0);

            if (!usable)
            {
                return new PhoneLookupResult(false, null, "This number is not a usable destination.");
            }

            return new PhoneLookupResult(true, response.PhoneNumber, null);
        }
        catch (SdkException<RawError> ex) when (IsCallerNumberRejection(ex.Error.StatusCode))
        {
            return new PhoneLookupResult(false, null, "This number is not a usable destination.");
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw Translate(ex);
        }
    }

    public Task<ProviderMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateMessageAsync(to, body, scheduleType: null, sendAt: null, cancellationToken);

    public Task<ProviderMessageSnapshot> ScheduleAsync(
        string to,
        string body,
        DateTimeOffset sendAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new NotificationProviderException(
                "Twilio:MessagingServiceSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        }

        return CreateMessageAsync(
            to,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            cancellationToken);
    }

    public async Task<ProviderMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken)
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
            return Map(message);
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw Translate(ex);
        }
    }

    public async Task<ProviderMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
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
                return Map(message);
            }
        }
        catch (TwilioDuplicateWritePreventedException ex)
        {
            throw new NotificationProviderException("The provider write outcome could not be confirmed.", inner: ex);
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw Translate(ex);
        }
    }

    public async Task<ProviderMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnceHandler.BeginWrite())
            {
                var message = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: "",
                        status: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
                return Map(message);
            }
        }
        catch (TwilioDuplicateWritePreventedException ex)
        {
            throw new NotificationProviderException("The provider write outcome could not be confirmed.", inner: ex);
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw Translate(ex);
        }
    }

    public async Task<(IReadOnlyList<ProviderMessageSnapshot> Messages, bool Truncated)> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken)
    {
        var messages = new List<ProviderMessageSnapshot>();
        string? pageToken = null;
        var pages = 0;
        var truncated = false;
        var fromBound = fromInclusive.ToUniversalTime().AddSeconds(-1);
        var toBound = toInclusive.ToUniversalTime().AddSeconds(1);

        try
        {
            while (true)
            {
                var page = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: toBound,
                        dateSentQueryQuery: fromBound,
                        pageSize: 1000,
                        page: null,
                        pageToken: pageToken,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                if (page.Messages is not null)
                {
                    messages.AddRange(page.Messages.Select(Map));
                }

                pages++;
                if (pages >= MaxListPages)
                {
                    truncated = !string.IsNullOrWhiteSpace(page.NextPageUri);
                    if (truncated)
                    {
                        _logger.LogWarning("Reconciliation listing stopped after {PageCap} pages.", MaxListPages);
                    }
                    break;
                }

                if (string.IsNullOrWhiteSpace(page.NextPageUri))
                    break;

                pageToken = TryReadPageToken(page.NextPageUri);
                if (string.IsNullOrWhiteSpace(pageToken))
                    break;
            }
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw Translate(ex);
        }

        return (messages, truncated);
    }

    private async Task<ProviderMessageSnapshot> CreateMessageAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var messagingServiceSid = string.IsNullOrWhiteSpace(_settings.MessagingServiceSid)
            ? null
            : _settings.MessagingServiceSid;

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
                return Map(message);
            }
        }
        catch (TwilioDuplicateWritePreventedException ex)
        {
            throw new NotificationProviderException("The provider write outcome could not be confirmed.", inner: ex);
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw Translate(ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static ProviderMessageSnapshot Map(ApiV2010AccountMessage message) =>
        new(
            message.Sid ?? string.Empty,
            message.Status?.Value ?? "unknown",
            message.Body,
            message.From,
            message.To,
            message.DateCreated,
            message.DateSent,
            message.ErrorCode,
            StripDestination(message.ErrorMessage));

    private NotificationProviderException Translate(Exception ex)
    {
        if (ex is SdkException<RawError> sdk)
        {
            var status = sdk.Error.StatusCode;
            _logger.LogWarning("Twilio API error HTTP {StatusCode} code {ProviderCode}", (int)status, TryReadCode(sdk.Error));

            if ((int)status is 401 or 403)
                return new NotificationProviderException("Provider unavailable.", status, sdk);
            if ((int)status == 429)
                return new NotificationProviderException("Temporarily unavailable.", status, sdk);
            if ((int)status >= 400 && (int)status < 500)
                return new NotificationProviderException("The provider rejected the request.", status, sdk);

            return new NotificationProviderException("Provider unavailable.", status, sdk);
        }

        if (ex is JsonException json)
        {
            _logger.LogWarning("Twilio response could not be processed: {ExceptionType}", json.GetType().Name);
            return new NotificationProviderException("The provider returned a response that could not be processed.", inner: json);
        }

        _logger.LogWarning("Twilio transport failure: {ExceptionType}", ex.GetType().Name);
        return new NotificationProviderException("provider unreachable", inner: ex);
    }

    private static int? TryReadCode(RawError error)
    {
        try
        {
            return error.ReadAsJson<TwilioApiErrorBody>()?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsCallerNumberRejection(HttpStatusCode statusCode) =>
        (int)statusCode is 400 or 404;

    private static string? TryReadPageToken(string nextPageUri)
    {
        if (!Uri.TryCreate(nextPageUri, UriKind.RelativeOrAbsolute, out var uri))
            return null;

        var query = uri.IsAbsoluteUri ? uri.Query : nextPageUri.Contains('?') ? nextPageUri[(nextPageUri.IndexOf('?') + 1)..] : string.Empty;
        if (string.IsNullOrEmpty(query))
            return null;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
                continue;
            if (parts[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase) ||
                parts[0].Equals("pageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static string? StripDestination(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return Regex.Replace(value, @"\+?\d[\d\s\-().]{6,}\d", "[redacted]");
    }
}
