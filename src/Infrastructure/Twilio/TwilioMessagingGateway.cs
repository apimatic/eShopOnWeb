using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(15);
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public Task<string> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken ct) =>
        ExecuteAsync(async deadline =>
        {
            var result = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: deadline);

            if (result.Valid != true || string.IsNullOrWhiteSpace(result.PhoneNumber))
            {
                throw new ContactNumberValidationException("The phone number is not a usable SMS destination.");
            }

            return result.PhoneNumber;
        }, ct);

    public Task<ProviderMessageState> SendAsync(string to, string body, CancellationToken ct) =>
        CreateAsync(to, body, null, ct);

    public async Task<ProviderMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var state = await CreateAsync(to, body, sendAt, ct);
        if (string.IsNullOrWhiteSpace(state.Sid))
        {
            throw new NotificationProviderException("Twilio did not identify the scheduled message.");
        }
        return state;
    }

    public Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken ct) =>
        ExecuteAsync(async deadline => Map(await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            requestOptions: null,
            ct: deadline)), ct);

    public Task<ProviderMessageState> CancelAsync(string providerMessageSid, CancellationToken ct) =>
        ExecuteWriteAsync(async deadline => Map(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: deadline)), ct);

    public async Task<ProviderMessageState> RedactAsync(string providerMessageSid, CancellationToken ct)
    {
        await ExecuteWriteAsync(async deadline => Map(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: string.Empty,
            status: null,
            requestOptions: null,
            ct: deadline)), ct);
        return await FetchAsync(providerMessageSid, ct);
    }

    public Task<IReadOnlyList<ProviderMessageState>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        ExecuteAsync(async deadline =>
        {
            const int maximumPages = 1000;
            var messages = new List<ProviderMessageState>();
            string? pageToken = null;

            for (var pageNumber = 0; pageNumber < maximumPages; pageNumber++)
            {
                var page = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: 1000,
                    page: null,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: deadline);

                if (page.Messages is not null)
                {
                    foreach (var message in page.Messages)
                    {
                        messages.Add(Map(message));
                    }
                }

                if (string.IsNullOrWhiteSpace(page.NextPageUri))
                {
                    return (IReadOnlyList<ProviderMessageState>)messages;
                }

                var nextToken = GetPageToken(page.NextPageUri);
                if (string.IsNullOrWhiteSpace(nextToken) || string.Equals(nextToken, pageToken, StringComparison.Ordinal))
                {
                    throw new NotificationProviderException("Twilio returned an invalid pagination continuation.");
                }

                pageToken = nextToken;
            }

            throw new NotificationProviderException("The reconciliation range exceeded the safe pagination limit; no partial report was returned.");
        }, ct);

    private Task<ProviderMessageState> CreateAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken ct) =>
        ExecuteWriteAsync(async deadline =>
        {
            var scheduled = sendAt.HasValue;
            var response = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: to,
                statusCallback: null,
                applicationSid: null,
                maxPrice: null,
                provideFeedback: null,
                attempt: null,
                validityPeriod: null,
                forceDelivery: null,
                contentRetention: MessageEnumContentRetention.Retain,
                addressRetention: null,
                smartEncoded: null,
                persistentAction: null,
                trafficType: null,
                shortenUrls: null,
                scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
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
                ct: deadline);

            return Map(response);
        }, ct);

    private static ProviderMessageState Map(ApiV2010AccountMessage message) => new(
        message.Sid,
        message.Status?.Value,
        message.ErrorCode,
        string.IsNullOrWhiteSpace(message.ErrorMessage) ? null : "Twilio reported a delivery error.",
        message.DateCreated,
        message.DateSent,
        message.DateUpdated,
        message.Price,
        message.PriceUnit,
        message.From,
        message.To,
        message.Body);

    private static string? GetPageToken(string nextPageUri)
    {
        var questionMark = nextPageUri.IndexOf('?');
        if (questionMark < 0 || questionMark == nextPageUri.Length - 1)
        {
            return null;
        }

        foreach (var part in nextPageUri[(questionMark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private static async Task<T> ExecuteWriteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var attempt = SingleAttemptHandler.BeginWrite();
        return await ExecuteAsync(call, ct);
    }

    private static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CallBudget);

        try
        {
            return await call(deadline.Token);
        }
        catch (ContactNumberValidationException)
        {
            throw;
        }
        catch (NotificationProviderException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            throw new NotificationProviderException(
                status is >= 400 and < 500 ? "Twilio rejected the request." : "Twilio is unavailable.",
                status,
                ex);
        }
        catch (DuplicateProviderWriteBlockedException ex)
        {
            throw new NotificationProviderException("The Twilio operation has an unknown outcome and was not repeated.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new NotificationProviderException("Twilio returned a response that could not be processed.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new NotificationProviderException("Twilio is unavailable.", null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new NotificationProviderException("Twilio did not respond before the operation deadline.", null, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new NotificationProviderException("Twilio returned an unexpected failure.", null, ex);
        }
    }
}
