using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of <see cref="IMessagingProvider"/> over the APIMatic-generated
/// Twilio SDK. All provider failures are converted to <see cref="MessagingProviderException"/>
/// at this boundary. Phone numbers and credentials are never logged.
/// </summary>
public class TwilioMessagingProvider : IMessagingProvider
{
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingProvider> _logger;

    public TwilioMessagingProvider(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioMessagingProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<VerifiedPhoneNumber?> VerifyPhoneNumberAsync(string phoneNumber, CancellationToken ct)
    {
        // Lookup is served from the lookups host and is NOT governed by Twilio:BaseUrl.
        var response = await Bounded(c => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
            ct: c), ct);

        if (response.Valid != true || string.IsNullOrWhiteSpace(response.PhoneNumber))
        {
            return null;
        }

        return new VerifiedPhoneNumber(response.PhoneNumber);
    }

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct)
    {
        // Exactly one sender parameter: the application's configured sending number.
        var message = await Bounded(c => _client.Api20100401Message.CreateMessage(
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
            ct: c), ct);

        return Map(message);
    }

    public async Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        // Scheduling is Messaging-Services-only, so the sender here is the messaging service.
        var message = await Bounded(c => _client.Api20100401Message.CreateMessage(
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
            ct: c), ct);

        return Map(message);
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageId, CancellationToken ct)
    {
        var message = await Bounded(c => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: c), ct);

        return Map(message);
    }

    public async Task<ProviderMessage> FetchMessageAsync(string providerMessageId, CancellationToken ct)
    {
        var message = await Bounded(c => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            ct: c), ct);

        return Map(message);
    }

    public async Task RedactMessageBodyAsync(string providerMessageId, CancellationToken ct)
    {
        // Blanking the body disposes of the text while the message record (and its
        // outcome) survives. DeleteMessage would destroy the record and is not used.
        await Bounded(c => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            body: "",
            status: null,
            ct: c), ct);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // The provider applies the From + date-sent-range filters server-side: only this
        // application's own sending number's traffic is ever returned.
        var all = new List<ProviderMessage>();
        string? pageToken = null;
        int? page = 0;
        var pages = 0;

        do
        {
            var currentToken = pageToken;
            var currentPage = page;
            var response = await Bounded(c => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: 1000,
                page: currentPage,
                pageToken: currentToken,
                ct: c), ct);

            if (response.Messages is not null)
            {
                all.AddRange(response.Messages.Select(Map));
            }

            pages++;
            pageToken = ExtractPageToken(response.NextPageUri);
            page = null;
        }
        while (pageToken is not null && pages < MaxListPages);

        if (pageToken is not null)
        {
            _logger.LogWarning("Reconciliation list hit the page cap of {MaxPages}; the report may be truncated.", MaxListPages);
        }

        return all;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var providerCode = TryReadProviderErrorCode(ex.Error);
            _logger.LogWarning(
                "Messaging provider rejected a request with HTTP {StatusCode} (provider error code {ProviderCode}).",
                (int)ex.Error.StatusCode, providerCode?.ToString(CultureInfo.InvariantCulture) ?? "none");

            throw new MessagingProviderException(
                $"The messaging provider rejected the request (HTTP {(int)ex.Error.StatusCode}).",
                ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            // A 2xx whose body does not match the SDK model: outcome unknown.
            throw new MessagingProviderException(
                "The messaging provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new MessagingProviderException("The messaging provider could not be reached.", null, ex);
        }
    }

    private static int? TryReadProviderErrorCode(RawError error)
    {
        try
        {
            return error.ReadAsJson<TwilioErrorPayload>()?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message)
    {
        return new ProviderMessage(
            message.Sid ?? string.Empty,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.To,
            message.From,
            ParseDate(message.DateSent));
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        foreach (var pair in nextPageUri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private sealed record TwilioErrorPayload([property: JsonPropertyName("code")] int? Code);
}
