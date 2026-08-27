using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioMessageProvider : ITwilioMessageProvider
{
    private const int MaxPages = 1000;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private readonly TwilioSdk.TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly SingleAttemptWriteGuard _writeGuard;

    public TwilioMessageProvider(
        TwilioSdk.TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        SingleAttemptWriteGuard writeGuard)
    {
        _client = client;
        _settings = settings.Value;
        _writeGuard = writeGuard;
    }

    public Task<PhoneValidationResult> ValidateDestinationAsync(string number, CancellationToken cancellationToken) =>
        GuardAsync(async ct =>
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: number,
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
                ct: ct);

            return new PhoneValidationResult(
                response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber),
                response.PhoneNumber);
        }, cancellationToken);

    public Task<ProviderMessageState> SendAsync(string canonicalNumber, string body, CancellationToken cancellationToken) =>
        GuardWriteAsync(ct => CreateAsync(canonicalNumber, body, null, ct), cancellationToken);

    public Task<ProviderMessageState> ScheduleAsync(string canonicalNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        GuardWriteAsync(ct => CreateAsync(canonicalNumber, body, sendAt, ct), cancellationToken);

    public Task<ProviderMessageState> CancelAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        GuardAsync(async ct => Map(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: null,
            status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: ct)), cancellationToken);

    public Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        GuardAsync(async ct => Map(await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            requestOptions: null,
            ct: ct)), cancellationToken);

    public Task<ProviderMessageState> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        GuardAsync(async ct =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: ct);

            var fetched = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                requestOptions: null,
                ct: ct);

            if (!string.IsNullOrEmpty(fetched.Body))
            {
                throw new MessagingProviderException(
                    "The provider did not confirm that message content was disposed.",
                    new InvalidOperationException("Provider content remained after redaction."));
            }

            return Map(fetched);
        }, cancellationToken);

    public Task<IReadOnlyList<ProviderMessageRecord>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        GuardAsync<IReadOnlyList<ProviderMessageRecord>>(async ct =>
        {
            var records = new List<ProviderMessageRecord>();
            for (var page = 0; page < MaxPages; page++)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: 1000,
                    page: page,
                    pageToken: null,
                    requestOptions: null,
                    ct: ct);

                foreach (var message in response.Messages ?? Array.Empty<TwilioSdk.Models.ApiV2010AccountMessage>())
                {
                    if (!string.IsNullOrWhiteSpace(message.Sid))
                    {
                        var state = Map(message);
                        records.Add(new ProviderMessageRecord(
                            message.Sid!, state.Status, state.DateCreated, state.DateUpdated,
                            state.DateSent, state.ErrorCode, state.ErrorMessage));
                    }
                }

                if (string.IsNullOrWhiteSpace(response.NextPageUri))
                {
                    return records;
                }
            }

            throw new MessagingProviderException(
                "The provider result exceeded the reconciliation safety limit; no partial report was returned.",
                new InvalidOperationException("Provider pagination did not terminate."));
        }, cancellationToken);

    private async Task<ProviderMessageState> CreateAsync(
        string canonicalNumber,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken ct)
    {
        var scheduled = sendAt.HasValue;
        var response = await _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: canonicalNumber,
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
            scheduleType: scheduled ? TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed : null,
            sendAt: sendAt,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: scheduled ? null : _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: scheduled ? _settings.MessagingServiceSid : null,
            body: body,
            mediaUrl: null,
            contentSid: null,
            requestOptions: null,
            ct: ct);

        if (string.IsNullOrWhiteSpace(response.Sid))
        {
            throw new MessagingProviderException(
                "The provider accepted the request without returning a message identifier.",
                new InvalidOperationException("Missing provider message identifier."));
        }

        return Map(response) with { ScheduledFor = sendAt };
    }

    private async Task<T> GuardWriteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var scope = _writeGuard.Begin();
        return await GuardAsync(operation, cancellationToken);
    }

    private static async Task<T> GuardAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);
        try
        {
            return await operation(deadline.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MessagingProviderException(
                SafeProviderMessage(ex.Error.StatusCode), (int)ex.Error.StatusCode, ex);
        }
        catch (DuplicateProviderWriteAttemptException ex)
        {
            throw new MessagingProviderException(
                "The provider write outcome is unknown; an automatic duplicate was prevented.", ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException(
                "The provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider is unavailable.", ex);
        }
    }

    private static string SafeProviderMessage(System.Net.HttpStatusCode statusCode) => statusCode switch
    {
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "The messaging provider is unavailable.",
        (System.Net.HttpStatusCode)429 => "The messaging provider is temporarily unavailable.",
        _ when (int)statusCode >= 400 && (int)statusCode < 500 => "The messaging provider rejected the request.",
        _ => "The messaging provider is unavailable."
    };

    private static ProviderMessageState Map(TwilioSdk.Models.ApiV2010AccountMessage message) => new(
        message.Sid,
        message.Status?.Value ?? "unknown",
        message.ErrorCode,
        message.ErrorMessage,
        ParseDate(message.DateCreated),
        ParseDate(message.DateUpdated),
        ParseDate(message.DateSent),
        Body: message.Body);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
