using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioMessageProvider(
    TwilioSdkClient client,
    IOptions<TwilioSettings> settings,
    ProviderWriteGuard writeGuard) : IMessageProvider
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private readonly TwilioSettings _settings = settings.Value;

    public async Task<string> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            LookupResponse response = await BoundedAsync(
                ct => client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
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
                    requestOptions: null,
                    ct: ct), cancellationToken);

            if (response.Valid != true || string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                throw new InvalidDestinationException("The provider does not consider this a usable destination.");
            }

            return response.PhoneNumber;
        }
        catch (SdkException<RawError> ex) when (IsCallerValidation(ex.Error.StatusCode))
        {
            throw new InvalidDestinationException("The provider does not consider this a usable destination.", ex);
        }
        catch (InvalidDestinationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw ConvertException(ex, "Destination validation is temporarily unavailable.");
        }
    }

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateAsync(to, body, null, false, cancellationToken);

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateAsync(to, body, sendAt, true, cancellationToken);

    public async Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = writeGuard.BeginScope();
            ApiV2010AccountMessage response = await BoundedAsync(
                ct => client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    requestOptions: null,
                    ct: ct), cancellationToken);
            return Map(response);
        }
        catch (Exception ex)
        {
            throw ConvertException(ex, "The scheduled message could not be cancelled at the provider.");
        }
    }

    public async Task<ProviderMessage> GetAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            ApiV2010AccountMessage response = await BoundedAsync(
                ct => client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    requestOptions: null,
                    ct: ct), cancellationToken);
            return Map(response);
        }
        catch (Exception ex)
        {
            throw ConvertException(ex, "The provider message state could not be read.");
        }
    }

    public async Task<ProviderMessage> DisposeContentAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (writeGuard.BeginScope())
            {
                await BoundedAsync(
                    ct => client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: string.Empty,
                        status: null,
                        requestOptions: null,
                        ct: ct), cancellationToken);
            }

            ApiV2010AccountMessage fetched = await BoundedAsync(
                ct => client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    requestOptions: null,
                    ct: ct), cancellationToken);

            if (!string.IsNullOrEmpty(fetched.Body))
            {
                throw new MessageProviderException("The provider did not confirm content disposal.");
            }

            return Map(fetched);
        }
        catch (MessageProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw ConvertException(ex, "Message content could not be disposed at the provider.");
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        const int maxPages = 1000;
        var messages = new List<ProviderMessage>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        try
        {
            for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
            {
                ListMessageResponse response = await BoundedAsync(
                    ct => client.Api20100401Message.ListMessage(
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
                        ct: ct), cancellationToken);

                foreach (ApiV2010AccountMessage message in response.Messages ?? [])
                {
                    ProviderMessage mapped = Map(message);
                    if (string.Equals(mapped.From, _settings.FromNumber, StringComparison.Ordinal) &&
                        mapped.DateSent is { } sent && sent >= from && sent <= to)
                    {
                        messages.Add(mapped);
                    }
                }

                if (string.IsNullOrWhiteSpace(response.NextPageUri))
                {
                    return messages;
                }

                string? nextToken = ReadQueryValue(response.NextPageUri, "PageToken");
                if (string.IsNullOrWhiteSpace(nextToken) || !seenTokens.Add(nextToken))
                {
                    throw new MessageProviderException("Provider pagination did not make progress.");
                }

                pageToken = nextToken;
            }

            throw new MessageProviderException("Provider pagination exceeded its safety limit.");
        }
        catch (MessageProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw ConvertException(ex, "The provider reconciliation records could not be read.");
        }
    }

    private async Task<ProviderMessage> CreateAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        bool scheduled,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = writeGuard.BeginScope();
            ApiV2010AccountMessage response = await BoundedAsync(
                ct => client.Api20100401Message.CreateMessage(
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
                    scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
                    sendAt: sendAt,
                    sendAsMms: null,
                    contentVariables: null,
                    riskCheck: null,
                    from: _settings.FromNumber,
                    fallbackFrom: null,
                    messagingServiceSid: scheduled ? _settings.MessagingServiceSid : null,
                    body: body,
                    mediaUrl: null,
                    contentSid: null,
                    requestOptions: null,
                    ct: ct), cancellationToken);
            return Map(response);
        }
        catch (Exception ex)
        {
            throw ConvertException(ex, "The message could not be submitted to the provider.");
        }
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid))
        {
            throw new JsonException("The provider response did not include a message identifier.");
        }

        return new ProviderMessage(
            message.Sid,
            message.Status?.Value ?? "unknown",
            message.Body,
            message.From,
            message.ErrorCode,
            ParseDate(message.DateCreated),
            ParseDate(message.DateSent),
            ParseDate(message.DateUpdated));
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;

    private static bool IsCallerValidation(HttpStatusCode statusCode) =>
        (int)statusCode is >= 400 and < 500 && statusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden && (int)statusCode != 429;

    private static MessageProviderException ConvertException(Exception exception, string safeMessage)
    {
        if (exception is MessageProviderException providerException)
        {
            return providerException;
        }

        if (exception is SdkException<RawError> sdkException)
        {
            return new MessageProviderException(safeMessage, sdkException.Error.StatusCode, sdkException);
        }

        return new MessageProviderException(safeMessage, null, exception);
    }

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallBudget);
        return await call(timeout.Token);
    }

    private static string? ReadQueryValue(string uri, string name)
    {
        int queryIndex = uri.IndexOf('?');
        if (queryIndex < 0 || queryIndex == uri.Length - 1)
        {
            return null;
        }

        foreach (string pair in uri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
