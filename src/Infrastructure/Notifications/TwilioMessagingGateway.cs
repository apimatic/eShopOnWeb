using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public interface ITwilioMessagingGateway
{
    Task<string> ValidateAndCanonicalizeAsync(string number, CancellationToken ct);
    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? scheduledFor, CancellationToken ct);
    Task<ProviderMessage> FetchAsync(string sid, CancellationToken ct);
    Task<ProviderMessage> CancelAsync(string sid, CancellationToken ct);
    Task<ProviderMessage> RedactAsync(string sid, CancellationToken ct);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record ProviderMessage(string Sid, string Status, int? ErrorCode,
    DateTimeOffset? CreatedAt, DateTimeOffset? SentAt, string? Body);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner) => StatusCode = statusCode;

    public HttpStatusCode? StatusCode { get; }
}

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private const int MaxReconciliationPages = 10_000;
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdkClient client, TwilioSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<string> ValidateAndCanonicalizeAsync(string number, CancellationToken ct)
    {
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: number, fields: null, countryCode: null, firstName: null,
                lastName: null, addressLine1: null, addressLine2: null, city: null,
                state: null, postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null, ct: ct);

            if (response.Valid != true || string.IsNullOrWhiteSpace(response.PhoneNumber))
                throw new TwilioProviderException("The provider does not consider this a valid destination.", HttpStatusCode.BadRequest);

            return response.PhoneNumber;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new TwilioProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TwilioProviderException("The provider could not be reached.", null, ex);
        }
    }

    public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? scheduledFor, CancellationToken ct) =>
        Call(async () => ToProviderMessage(await _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid, to: to, statusCallback: null, applicationSid: null,
            maxPrice: null, provideFeedback: null, attempt: null, validityPeriod: null,
            forceDelivery: null, contentRetention: null, addressRetention: null,
            smartEncoded: null, persistentAction: null, trafficType: null, shortenUrls: null,
            scheduleType: scheduledFor.HasValue ? MessageEnumScheduleType.Fixed : null,
            sendAt: scheduledFor, sendAsMms: null, contentVariables: null, riskCheck: null,
            from: _settings.FromNumber, fallbackFrom: null,
            messagingServiceSid: _settings.MessagingServiceSid, body: body, mediaUrl: null,
            contentSid: null, ct: ct)));

    public Task<ProviderMessage> FetchAsync(string sid, CancellationToken ct) =>
        Call(async () => ToProviderMessage(await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid, sid: sid, ct: ct)));

    public Task<ProviderMessage> CancelAsync(string sid, CancellationToken ct) =>
        Call(async () => ToProviderMessage(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid, sid: sid, body: null,
            status: MessageEnumUpdateStatus.Canceled, ct: ct)));

    public Task<ProviderMessage> RedactAsync(string sid, CancellationToken ct) =>
        Call(async () => ToProviderMessage(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid, sid: sid, body: string.Empty, status: null, ct: ct)));

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var messages = new List<ProviderMessage>();
            string? pageToken = null;
            string? previousNextPage = null;

            for (var pageCount = 0; pageCount < MaxReconciliationPages; pageCount++)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid, to: null, from: _settings.FromNumber,
                    dateSent: null, dateSentQuery: to, dateSentQueryQuery: from,
                    pageSize: 1000, page: null, pageToken: pageToken, ct: ct);

                messages.AddRange((response.Messages ?? Array.Empty<ApiV2010AccountMessage>())
                    .Select(ToProviderMessage));

                if (string.IsNullOrWhiteSpace(response.NextPageUri))
                    return messages;
                if (string.Equals(previousNextPage, response.NextPageUri, StringComparison.Ordinal))
                    throw new TwilioProviderException("Provider pagination made no progress.");

                previousNextPage = response.NextPageUri;
                pageToken = ReadQueryParameter(response.NextPageUri, "PageToken");
                if (string.IsNullOrWhiteSpace(pageToken))
                    throw new TwilioProviderException("Provider pagination did not include a page token.");
            }

            throw new TwilioProviderException("Provider pagination exceeded the safety limit; no partial report was returned.");
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new TwilioProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TwilioProviderException("The provider could not be reached.", null, ex);
        }
    }

    private static async Task<ProviderMessage> Call(Func<Task<ProviderMessage>> call)
    {
        try { return await call(); }
        catch (SdkException<RawError> ex) { throw Translate(ex); }
        catch (JsonException ex) { throw new TwilioProviderException("The provider returned a response that could not be processed.", null, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { throw new TwilioProviderException("The provider could not be reached.", null, ex); }
    }

    private static TwilioProviderException Translate(SdkException<RawError> ex)
    {
        var status = ex.Error.StatusCode;
        var message = status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity
            ? "The provider rejected the request."
            : "The messaging provider is unavailable.";
        return new TwilioProviderException(message, status, ex);
    }

    private static ProviderMessage ToProviderMessage(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid))
            throw new TwilioProviderException("The provider response omitted the message identifier.");

        return new ProviderMessage(message.Sid, message.Status?.Value ?? "unknown", message.ErrorCode,
            ParseDate(message.DateCreated), ParseDate(message.DateSent), message.Body);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var result)
            ? result : null;

    private static string? ReadQueryParameter(string uri, string name)
    {
        var question = uri.IndexOf('?');
        if (question < 0) return null;
        foreach (var part in uri[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length == 2 && string.Equals(Uri.UnescapeDataString(pieces[0]), name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
        }
        return null;
    }
}
