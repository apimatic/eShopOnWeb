using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioMessagingService(
    TwilioSdk.TwilioSdkClient client,
    IOptions<TwilioSettings> settings,
    TwilioRequestContext requestContext) : ITwilioMessagingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(12);
    private readonly TwilioSettings _settings = settings.Value;

    public async Task<string?> ValidateAndCanonicalizeAsync(string input, CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            token => client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: input,
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
                ct: token),
            singleNetworkAttempt: false,
            cancellationToken);

        return response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber)
            ? response.PhoneNumber
            : null;
    }

    public Task<ProviderMessage> SendAsync(string canonicalDestination, string body, CancellationToken cancellationToken) =>
        CreateAsync(canonicalDestination, body, null, false, cancellationToken);

    public Task<ProviderMessage> ScheduleAsync(string canonicalDestination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateAsync(canonicalDestination, body, sendAt, true, cancellationToken);

    public async Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            token => client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: token),
            singleNetworkAttempt: false,
            cancellationToken);
        return Map(response);
    }

    public async Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            token => client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: token),
            singleNetworkAttempt: true,
            cancellationToken);
        return Map(response);
    }

    public async Task<ProviderMessage> RedactAsync(string providerSid, CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            token => client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: token),
            singleNetworkAttempt: true,
            cancellationToken);
        return Map(response);
    }

    public async Task<ProviderMessagePage> ListAsync(
        DateTimeOffset fromExclusive,
        DateTimeOffset toExclusive,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            token => client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: toExclusive,
                dateSentQueryQuery: fromExclusive,
                pageSize: 1000,
                page: null,
                pageToken: pageToken,
                requestOptions: null,
                ct: token),
            singleNetworkAttempt: false,
            cancellationToken);

        var messages = response.Messages?.Select(Map).ToList() ?? [];
        return new ProviderMessagePage(messages, ParsePageToken(response.NextPageUri));
    }

    private async Task<ProviderMessage> CreateAsync(
        string canonicalDestination,
        string body,
        DateTimeOffset? sendAt,
        bool scheduled,
        CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            token => client.Api20100401Message.CreateMessage(
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
                ct: token),
            singleNetworkAttempt: true,
            cancellationToken);
        return Map(response);
    }

    private async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> call,
        bool singleNetworkAttempt,
        CancellationToken cancellationToken)
    {
        using var requestScope = requestContext.Begin(singleNetworkAttempt);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await call(budget.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex.Error.StatusCode, ex);
        }
        catch (TwilioDuplicateAttemptBlockedException ex)
        {
            throw new TwilioProviderException(
                "The provider outcome is unknown; an automatic duplicate attempt was prevented.",
                ambiguous: true,
                innerException: ex);
        }
        catch (JsonException ex)
        {
            var status = requestContext.Current?.LastStatusCode;
            if (status is >= HttpStatusCode.BadRequest)
                throw ToProviderException(status.Value, ex);

            throw new TwilioProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TwilioProviderException(
                singleNetworkAttempt
                    ? "The provider outcome is unknown; an automatic duplicate attempt was prevented."
                    : "The messaging provider is unavailable.",
                ambiguous: singleNetworkAttempt,
                innerException: ex);
        }
    }

    private static TwilioProviderException ToProviderException(HttpStatusCode statusCode, Exception innerException)
    {
        var safeMessage = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The messaging provider rejected this application's credentials.",
            HttpStatusCode.TooManyRequests => "The messaging provider is temporarily unavailable.",
            >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => "The messaging provider rejected the request.",
            _ => "The messaging provider is unavailable."
        };
        return new TwilioProviderException(safeMessage, statusCode, innerException: innerException);
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message) => new(
        message.Sid,
        message.From,
        message.Body,
        message.Status?.Value,
        message.ErrorCode,
        message.ErrorMessage,
        ParseProviderDate(message.DateCreated),
        ParseProviderDate(message.DateSent),
        ParseProviderDate(message.DateUpdated));

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static string? ParsePageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri)) return null;
        var question = nextPageUri.IndexOf('?');
        if (question < 0 || question == nextPageUri.Length - 1) return null;

        foreach (var pair in nextPageUri[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }

        return null;
    }
}
