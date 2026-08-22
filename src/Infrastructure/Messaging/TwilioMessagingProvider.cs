using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed class TwilioMessagingProvider : IMessagingProvider
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(15);
    private const int MaxListPages = 50;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingProvider> _logger;

    public TwilioMessagingProvider(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioMessagingProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken ct)
    {
        var lookup = await InvokeAsync(
            token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: token),
            ct);

        var validationErrors = lookup.ValidationErrors;
        var hasValidationErrors = validationErrors != null && validationErrors.Count > 0;
        var canonical = lookup.PhoneNumber;
        var explicitlyInvalid = lookup.Valid == false;
        var isUsable = !explicitlyInvalid
            && !hasValidationErrors
            && !string.IsNullOrEmpty(canonical);

        var lineType = lookup.LineTypeIntelligence?.ErrorCode == null
            ? lookup.LineTypeIntelligence?.Type
            : null;

        _logger.LogInformation(
            "Phone lookup completed. valid={Valid} hasCanonical={HasCanonical} validationErrorCount={ValidationErrorCount} lineTypeError={LineTypeError}.",
            lookup.Valid,
            !string.IsNullOrEmpty(canonical),
            hasValidationErrors ? validationErrors!.Count : 0,
            lookup.LineTypeIntelligence?.ErrorCode);

        return new PhoneLookupResult(
            isUsable,
            canonical,
            lineType,
            isUsable ? null : "The provider does not consider this a usable destination.");
    }

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken ct) =>
        CreateMessageAsync(to, body, scheduleType: null, sendAt: null, from: _settings.FromNumber, messagingServiceSid: null, ct);

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        CreateMessageAsync(
            to,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            from: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            ct);

    public async Task<ProviderMessage> CancelScheduledAsync(string providerSid, CancellationToken ct)
    {
        var message = await InvokeAsync(
            token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: token),
            ct);
        return Map(message);
    }

    public async Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken ct)
    {
        var message = await InvokeAsync(
            token => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: token),
            ct);
        return Map(message);
    }

    public async Task<ProviderMessage> RedactBodyAsync(string providerSid, CancellationToken ct)
    {
        var message = await InvokeAsync(
            token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: "",
                status: null,
                requestOptions: null,
                ct: token),
            ct);
        return Map(message);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken ct)
    {
        var results = new List<ProviderMessage>();
        string? pageToken = null;
        int? page = null;
        var pages = 0;

        while (pages < MaxListPages)
        {
            var capturedPage = page;
            var capturedToken = pageToken;
            var response = await InvokeAsync(
                token => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: toExclusive,
                    dateSentQueryQuery: fromInclusive,
                    pageSize: 1000L,
                    page: capturedPage,
                    pageToken: capturedToken,
                    requestOptions: null,
                    ct: token),
                ct);

            pages++;
            if (response.Messages != null)
            {
                results.AddRange(response.Messages.Select(Map));
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }

            (page, pageToken) = ParseNextPage(response.NextPageUri);
            if (page == null && string.IsNullOrEmpty(pageToken))
            {
                page = (response.Page ?? 0) + 1;
            }
        }

        if (pages >= MaxListPages)
        {
            _logger.LogWarning("Reconciliation list stopped after {PageCap} pages.", MaxListPages);
        }

        return results;
    }

    private async Task<ProviderMessage> CreateMessageAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? from,
        string? messagingServiceSid,
        CancellationToken ct)
    {
        using (TwilioCreateWriteScope.Begin())
        {
            var created = await InvokeAsync(
                token => _client.Api20100401Message.CreateMessage(
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
                    ct: token),
                ct);

            _logger.LogInformation(
                "CreateMessage completed with provider status {Status} and sid present {HasSid}.",
                created.Status?.Value,
                !string.IsNullOrEmpty(created.Sid));
            return Map(created);
        }
    }

    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Twilio returned HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            throw new MessagingProviderException(
                "The messaging provider rejected the request.",
                (int)ex.Error.StatusCode,
                ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException(
                "The provider returned a response that could not be processed.",
                inner: ex);
        }
        catch (DuplicateTwilioWriteException ex)
        {
            throw new MessagingProviderException(
                "The send outcome is unknown because a duplicate create was blocked.",
                inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new MessagingProviderException("The messaging provider is unreachable.", inner: ex);
        }
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message) => new(
        message.Sid,
        message.Status?.Value,
        message.ErrorCode,
        message.ErrorMessage,
        message.Body,
        message.From,
        message.To,
        message.DateSent,
        message.DateCreated,
        message.MessagingServiceSid);

    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        Uri uri;
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            uri = absolute;
        }
        else if (Uri.TryCreate("https://api.twilio.com" + (nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri), UriKind.Absolute, out var relative))
        {
            uri = relative;
        }
        else
        {
            return (null, null);
        }

        int? page = null;
        string? pageToken = null;
        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query))
        {
            return (null, null);
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[0]);
            var value = Uri.UnescapeDataString(pair[1]);
            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var parsedPage))
            {
                page = parsedPage;
            }
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
            }
        }

        return (page, pageToken);
    }
}
