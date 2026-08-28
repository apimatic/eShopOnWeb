using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public Task<ProviderPhoneValidation> ValidatePhoneNumberAsync(string submittedNumber, CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: submittedNumber,
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
                ct: ct);

            return new ProviderPhoneValidation(response.Valid == true, response.PhoneNumber);
        }, cancellationToken);

    public Task<ProviderMessage> SendImmediateAsync(string canonicalDestination, string body, CancellationToken cancellationToken) =>
        BoundedWriteAsync(async ct => Map(await _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: canonicalDestination,
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
            ct: ct)), cancellationToken);

    public Task<ProviderMessage> ScheduleAsync(string canonicalDestination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        BoundedWriteAsync(async ct => Map(await _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: canonicalDestination,
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
            ct: ct)), cancellationToken);

    public Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken) =>
        BoundedAsync(async ct => Map(await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerSid,
            ct: ct)), cancellationToken);

    public Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken) =>
        BoundedWriteAsync(async ct => Map(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: ct)), cancellationToken);

    public Task<ProviderMessage> RedactAsync(string providerSid, CancellationToken cancellationToken) =>
        BoundedWriteAsync(async ct =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: string.Empty,
                status: null,
                ct: ct);
            return Map(await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                ct: ct));
        }, cancellationToken);

    public Task<ProviderMessagePage> ListAsync(
        DateTimeOffset widenedLower,
        DateTimeOffset widenedUpper,
        string? pageToken,
        CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            var response = await _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: widenedUpper,
                dateSentQueryQuery: widenedLower,
                pageSize: 1000,
                page: null,
                pageToken: pageToken,
                ct: ct);

            var messages = new List<ProviderMessage>();
            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    messages.Add(Map(message));
                }
            }

            return new ProviderMessagePage(messages, ExtractPageToken(response.NextPageUri));
        }, cancellationToken);

    private async Task<T> BoundedWriteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var scope = TwilioWriteOnceHandler.BeginScope();
        return await BoundedAsync(call, cancellationToken);
    }

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);

        try
        {
            return await call(deadline.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MessagingProviderException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The messaging provider returned an unreadable response.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TwilioDuplicateWriteBlockedException)
        {
            throw new MessagingProviderException("The messaging provider could not complete the request.", null, ex);
        }
    }

    private static ProviderMessage Map(TwilioSdk.Models.ApiV2010AccountMessage message) =>
        new(
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.Body,
            message.From,
            ParseDate(message.DateCreated),
            ParseDate(message.DateSent));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var queryStart = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || queryStart == nextPageUri.Length - 1)
        {
            return null;
        }

        foreach (var segment in nextPageUri[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            }
        }

        return null;
    }
}
