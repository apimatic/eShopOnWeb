using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsNotificationGateway : ISmsNotificationGateway
{
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconciliationBudget = TimeSpan.FromSeconds(60);
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;

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

    public async Task<SmsLookupResult> LookupDestinationAsync(string phoneNumber, CancellationToken cancellationToken = default)
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
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            var errors = response.ValidationErrors?
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToList() ?? new List<string>();

            var hasCanonical = !string.IsNullOrWhiteSpace(response.PhoneNumber);
            var usable = hasCanonical && (
                response.Valid == true
                || (response.Valid is null && errors.Count == 0));

            _logger.LogInformation(
                "Phone lookup completed. usable={Usable} valid={Valid} hasCanonical={HasCanonical} validationErrorCount={ErrorCount}.",
                usable,
                response.Valid,
                hasCanonical,
                errors.Count);

            return new SmsLookupResult
            {
                IsUsable = usable,
                CanonicalNumber = response.PhoneNumber,
                ValidationErrors = errors
            };
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode is 400 or 404)
        {
            return new SmsLookupResult
            {
                IsUsable = false,
                ValidationErrors = new[] { "The provider rejected this number." }
            };
        }
        catch (Exception ex)
        {
            throw MapFailure(ex, "lookup");
        }
    }

    public Task<SmsMessageSnapshot> SendNowAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        return CreateAsync(
            to: to,
            body: body,
            from: _settings.FromNumber,
            messagingServiceSid: null,
            scheduleType: null,
            sendAt: null,
            cancellationToken: cancellationToken);
    }

    public Task<SmsMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        return CreateAsync(
            to: to,
            body: body,
            from: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt.ToUniversalTime(),
            cancellationToken: cancellationToken);
    }

    public async Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
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
            return MapMessage(message);
        }
        catch (Exception ex)
        {
            throw MapFailure(ex, "fetch");
        }
    }

    public async Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await FetchAsync(providerSid, cancellationToken);
            if (!string.Equals(current.Status, MessageEnumStatus.Scheduled.Value, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            using (SingleAttemptWriteHandler.BeginScope())
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
                return MapMessage(updated);
            }
        }
        catch (Exception ex) when (ex is not SmsProviderException)
        {
            throw MapFailure(ex, "cancel");
        }
    }

    public async Task<SmsMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        try
        {
            using (SingleAttemptWriteHandler.BeginScope())
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
                return MapMessage(updated);
            }
        }
        catch (Exception ex)
        {
            throw MapFailure(ex, "redact");
        }
    }

    public async Task<SmsReconciliationPage> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        var pages = 0;
        var complete = true;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ReconciliationBudget);
            var deadline = cts.Token;

            while (true)
            {
                if (++pages > MaxListPages)
                {
                    complete = false;
                    _logger.LogWarning("Reconciliation list hit the page cap of {MaxPages}.", MaxListPages);
                    break;
                }

                var page = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to.ToUniversalTime(),
                    dateSentQueryQuery: from.ToUniversalTime(),
                    pageSize: ListPageSize,
                    page: null,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: deadline);

                if (page.Messages is not null)
                {
                    foreach (var message in page.Messages)
                    {
                        messages.Add(MapMessage(message));
                    }
                }

                if (string.IsNullOrWhiteSpace(page.NextPageUri))
                {
                    break;
                }

                var nextToken = ExtractPageToken(page.NextPageUri);
                if (string.IsNullOrWhiteSpace(nextToken) || string.Equals(nextToken, pageToken, StringComparison.Ordinal))
                {
                    complete = false;
                    _logger.LogWarning("Reconciliation paging stopped because the page token did not advance.");
                    break;
                }

                pageToken = nextToken;
            }
        }
        catch (Exception ex)
        {
            throw MapFailure(ex, "list");
        }

        return new SmsReconciliationPage
        {
            Messages = messages,
            Complete = complete,
            FromNumber = _settings.FromNumber
        };
    }

    private async Task<SmsMessageSnapshot> CreateAsync(
        string to,
        string body,
        string? from,
        string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using (SingleAttemptWriteHandler.BeginScope())
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
                        from: from,
                        fallbackFrom: null,
                        messagingServiceSid: messagingServiceSid,
                        body: body,
                        mediaUrl: null,
                        contentSid: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
                return MapMessage(created);
            }
        }
        catch (Exception ex)
        {
            throw MapFailure(ex, "create");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultBudget);
        return await call(cts.Token);
    }

    private SmsProviderException MapFailure(Exception ex, string operation)
    {
        if (ex is SmsProviderException already)
        {
            return already;
        }

        if (ex is DuplicateProviderWriteException)
        {
            _logger.LogWarning("A duplicate {Operation} write was blocked after the first attempt.", operation);
            return new SmsProviderException(
                "The provider outcome is unknown because a retried write was blocked.",
                SmsProviderFailureKind.OutcomeUnknown,
                inner: ex);
        }

        if (ex is SdkException<RawError> sdk)
        {
            var status = (int)sdk.Error.StatusCode;
            var kind = status switch
            {
                401 or 403 => SmsProviderFailureKind.ProviderUnavailable,
                429 => SmsProviderFailureKind.RateLimited,
                >= 400 and < 500 => SmsProviderFailureKind.CallerRejected,
                _ => SmsProviderFailureKind.ProviderUnavailable
            };
            _logger.LogWarning("SMS provider {Operation} failed with HTTP {Status}.", operation, status);
            return new SmsProviderException($"SMS provider {operation} failed with HTTP {status}.", kind, status, sdk);
        }

        if (ex is JsonException json)
        {
            var last = LastHttpStatusHandler.LastStatus;
            if (last is HttpStatusCode code && (int)code >= 400)
            {
                var status = (int)code;
                var kind = status switch
                {
                    401 or 403 => SmsProviderFailureKind.ProviderUnavailable,
                    429 => SmsProviderFailureKind.RateLimited,
                    >= 400 and < 500 => SmsProviderFailureKind.CallerRejected,
                    _ => SmsProviderFailureKind.ProviderUnavailable
                };
                _logger.LogWarning("SMS provider {Operation} rejected the request (HTTP {Status}) with an unreadable error body.", operation, status);
                return new SmsProviderException($"SMS provider {operation} rejected the request.", kind, status, json);
            }

            _logger.LogWarning("SMS provider {Operation} returned a response that could not be processed.", operation);
            return new SmsProviderException(
                "The provider returned a response that could not be processed.",
                SmsProviderFailureKind.OutcomeUnknown,
                inner: json);
        }

        if (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("SMS provider {Operation} was unreachable or timed out.", operation);
            return new SmsProviderException("The SMS provider was unreachable.", SmsProviderFailureKind.ProviderUnavailable, inner: ex);
        }

        _logger.LogWarning("SMS provider {Operation} failed unexpectedly.", operation);
        return new SmsProviderException("The SMS provider call failed.", SmsProviderFailureKind.ProviderUnavailable, inner: ex);
    }

    private static SmsMessageSnapshot MapMessage(ApiV2010AccountMessage message) => new()
    {
        ProviderSid = message.Sid,
        Status = message.Status?.Value ?? "unknown",
        Body = message.Body,
        To = message.To,
        From = message.From,
        DateSent = message.DateSent,
        ErrorCode = message.ErrorCode,
        ErrorMessage = PhoneNumberSanitizer.Redact(message.ErrorMessage)
    };

    private static string? ExtractPageToken(string nextPageUri)
    {
        var qIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (qIndex < 0 || qIndex == nextPageUri.Length - 1)
        {
            return null;
        }

        var query = nextPageUri[(qIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(Uri.UnescapeDataString(kv[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }
}
