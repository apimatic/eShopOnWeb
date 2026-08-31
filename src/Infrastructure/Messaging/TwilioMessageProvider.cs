using System;
using System.Collections.Generic;
using System.Globalization;
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
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of <see cref="IMessageProvider"/> over the APIMatic-generated
/// Twilio SDK. All contract facts (signatures, wire names, enum values) come from
/// twilio-plan.md. Destination numbers and credentials are never written to logs or
/// exception messages.
/// </summary>
public class TwilioMessageProvider : IMessageProvider
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 100;
    private const long ListPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessageProvider> _logger;

    public TwilioMessageProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings,
        ILogger<TwilioMessageProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ProviderValidatedNumber> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var response = await Bounded(ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                requestOptions: null,
                ct: ct), ct);

            if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
            {
                return new ProviderValidatedNumber
                {
                    IsValid = true,
                    CanonicalNumber = response.PhoneNumber
                };
            }

            return new ProviderValidatedNumber
            {
                IsValid = false,
                ValidationErrors = response.ValidationErrors?
                    .Select(e => e.Value)
                    .ToArray() ?? Array.Empty<string>()
            };
        }
        catch (SdkException<RawError> ex) when (IsCallerRejection(ex.Error.StatusCode))
        {
            // The provider rejected the lookup itself: not a usable destination.
            return new ProviderValidatedNumber { IsValid = false };
        }
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct = default)
        => CreateMessage(to, body, schedule: null, ct);

    public Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default)
        => CreateMessage(to, body, sendAt, ct);

    private async Task<ProviderMessage> CreateMessage(string to, string body, DateTimeOffset? schedule, CancellationToken ct)
    {
        // Scheduled sends are a Messaging-Services-only capability, so a scheduled message
        // goes out via the messaging service and an immediate one via the From number.
        var message = await Guarded(ct => _client.Api20100401Message.CreateMessage(
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
            scheduleType: schedule.HasValue ? MessageEnumScheduleType.Fixed : null,
            sendAt: schedule,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: schedule.HasValue ? null : _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: schedule.HasValue ? _settings.MessagingServiceSid : null,
            body: body,
            mediaUrl: null,
            contentSid: null,
            requestOptions: null,
            ct: ct), ct);

        var result = Map(message);
        _logger.LogInformation("Message {MessageSid} accepted by provider with status {Status}", result.Sid, result.Status);
        return result;
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken ct = default)
    {
        var message = await Guarded(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: ct), ct);

        var result = Map(message);
        _logger.LogInformation("Scheduled message {MessageSid} cancelled at provider, status {Status}", result.Sid, result.Status);
        return result;
    }

    public async Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken ct = default)
    {
        var message = await Guarded(ct => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            requestOptions: null,
            ct: ct), ct);

        return Map(message);
    }

    public async Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken ct = default)
    {
        await Guarded(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: string.Empty,
            status: null,
            requestOptions: null,
            ct: ct), ct);

        _logger.LogInformation("Message {MessageSid} body redacted at provider", providerMessageSid);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<ProviderMessage>();
        string? pageToken = null;
        int? page = null;

        for (var pageCount = 0; pageCount < MaxListPages; pageCount++)
        {
            var response = await Guarded(ct => _client.Api20100401Message.ListMessage(
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
                ct: ct), ct);

            if (response.Messages is not null)
            {
                results.AddRange(response.Messages.Select(Map));
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                return results;
            }

            pageToken = ExtractQueryParam(response.NextPageUri, "PageToken");
            page = null;
            if (pageToken is null)
            {
                _logger.LogWarning("Provider returned a next page without a page token; stopping pagination early");
                return results;
            }
        }

        _logger.LogWarning("Reconciliation listing hit the {MaxPages}-page cap; report may be truncated", MaxListPages);
        return results;
    }

    private async Task<T> Guarded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await Bounded(call, ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MessageProviderException("The messaging provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessageProviderException("The messaging provider could not be reached.", null, ex);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static bool IsCallerRejection(HttpStatusCode status)
        => (int)status >= 400 && (int)status < 500 && status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);

    private static MessageProviderException ToProviderException(HttpStatusCode status, Exception inner)
        => (int)status switch
        {
            401 or 403 => new MessageProviderException("The messaging provider rejected this application's credentials.", status, inner),
            429 => new MessageProviderException("The messaging provider is throttling requests.", status, inner),
            >= 400 and < 500 => new MessageProviderException($"The messaging provider rejected the request (status {(int)status}).", status, inner),
            _ => new MessageProviderException("The messaging provider is unavailable.", status, inner)
        };

    private static ProviderMessage Map(TwilioSdk.Models.ApiV2010AccountMessage message)
        => new()
        {
            Sid = message.Sid,
            Status = message.Status?.Value,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            From = message.From,
            To = message.To,
            DateSent = ParseDate(message.DateSent),
            DateCreated = ParseDate(message.DateCreated)
        };

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string? ExtractQueryParam(string uri, string name)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var query = parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in query)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
