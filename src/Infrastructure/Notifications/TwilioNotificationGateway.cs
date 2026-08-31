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

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Twilio-backed implementation of the messaging provider boundary.
/// Phone numbers and the auth token are never written to logs; only message SIDs,
/// statuses and HTTP status codes are.
/// </summary>
public class TwilioNotificationGateway : INotificationGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ReconciliationPageSize = 100;
    private const int MaxReconciliationPages = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioNotificationGateway> _logger;

    public TwilioNotificationGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioNotificationGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            throw new InvalidPhoneNumberException("A phone number is required.");
        }

        try
        {
            var lookup = await Bounded(ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
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
                ct: ct), cancellationToken);

            if (lookup.Valid == true && !string.IsNullOrEmpty(lookup.PhoneNumber))
            {
                return new ValidatedPhoneNumber(lookup.PhoneNumber);
            }

            throw new InvalidPhoneNumberException("The phone number is not a usable destination.");
        }
        catch (NotificationProviderException ex) when ((int?)ex.StatusCode is >= 400 and < 500)
        {
            // The provider itself rejects this destination (a 4xx on the lookup).
            throw new InvalidPhoneNumberException("The phone number is not a usable destination.");
        }
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
        => CreateMessage(to, body, from: _settings.FromNumber, messagingServiceSid: null, sendAt: null, cancellationToken);

    public Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
        => CreateMessage(to, body, from: null, messagingServiceSid: _settings.MessagingServiceSid, sendAt: sendAt, cancellationToken);

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var updated = await Bounded(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: ct), cancellationToken);

        return ToProviderMessage(updated);
    }

    public async Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var message = await Bounded(ct => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            ct: ct), cancellationToken);

        return ToProviderMessage(message);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        string? pageToken = null;
        int? page = null;

        for (var pageCount = 0; pageCount < MaxReconciliationPages; pageCount++)
        {
            // dateSentQuery is the UPPER bound (DateSent<), dateSentQueryQuery the LOWER bound (DateSent>).
            var response = await Bounded(ct => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: ReconciliationPageSize,
                page: page,
                pageToken: pageToken,
                ct: ct), cancellationToken);

            if (response.Messages != null)
            {
                results.AddRange(response.Messages.Select(ToProviderMessage));
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                return results;
            }

            pageToken = TryParseQueryValue(response.NextPageUri, "PageToken");
            if (pageToken == null)
            {
                // Fallback: advance by page number when the token cannot be parsed.
                page = (response.Page ?? 0) + 1;
            }
        }

        _logger.LogWarning("Reconciliation listing hit the page cap of {MaxPages}; the result may be truncated.", MaxReconciliationPages);
        return results;
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        await Bounded(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            body: string.Empty,
            status: null,
            ct: ct), cancellationToken);
    }

    private async Task<ProviderMessage> CreateMessage(string to, string body, string? from, string? messagingServiceSid,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var message = await Bounded(ct => _client.Api20100401Message.CreateMessage(
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
            scheduleType: sendAt.HasValue ? MessageEnumScheduleType.Fixed : null,
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
            ct: ct), cancellationToken);

        if (string.IsNullOrEmpty(message.Sid))
        {
            throw new NotificationProviderException("The provider accepted the send request but returned no message identifier.");
        }

        return ToProviderMessage(message);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Twilio call failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            throw new NotificationProviderException(
                $"The messaging provider rejected the request (HTTP {(int)ex.Error.StatusCode}).",
                ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            // A 2xx whose body drifted from the SDK model: outcome unknown, not a rejection.
            throw new NotificationProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new NotificationProviderException("The messaging provider could not be reached.", null, ex);
        }
    }

    private static ProviderMessage ToProviderMessage(TwilioSdk.Models.ApiV2010AccountMessage message)
        => new(
            message.Sid ?? string.Empty,
            message.Status?.Value ?? "unknown",
            message.ErrorCode,
            message.ErrorMessage,
            ParseDate(message.DateSent));

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string? TryParseQueryValue(string uri, string key)
    {
        var queryStart = uri.IndexOf('?');
        if (queryStart < 0)
        {
            return null;
        }

        foreach (var pair in uri[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
