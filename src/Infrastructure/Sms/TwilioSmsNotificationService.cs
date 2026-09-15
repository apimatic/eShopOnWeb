using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio-backed implementation of <see cref="ISmsNotificationService"/>. It is the single
/// boundary between the app and the Twilio SDK: every SDK call is translated here into either a
/// plain result DTO or a caller-safe <see cref="SmsNotificationException"/> carrying the provider's
/// HTTP status. Neither the auth token nor any destination number is ever logged or surfaced.
/// </summary>
public class TwilioSmsNotificationService : ISmsNotificationService
{
    // A safety cap so the reconciliation pagination loop can never spin on a provider that keeps
    // handing out a next page. 200 pages * 1000/page covers any realistic range for this app.
    private const int MaxReconciliationPages = 200;
    private const long ReconciliationPageSize = 1000;

    private static readonly JsonSerializerOptions ErrorJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsNotificationService(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            return new PhoneNumberValidationResult(false, null, "A phone number is required.");

        try
        {
            LookupResponse response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: cancellationToken);

            if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
                return new PhoneNumberValidationResult(true, response.PhoneNumber, null);

            return new PhoneNumberValidationResult(false, null, DescribeValidationErrors(response));
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode is 400 or 404)
        {
            // The provider positively considers this an unusable/unknown destination — reject it here.
            return new PhoneNumberValidationResult(false, null, "The number is not a valid, reachable destination.");
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "number lookup");
        }
        catch (JsonException ex)
        {
            throw new SmsNotificationException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsNotificationException("The SMS provider is currently unreachable.", null, ex);
        }
    }

    public Task<SentMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default) =>
        CreateMessageAsync(toNumber, body, from: _settings.FromNumber, messagingServiceSid: null,
            scheduleType: null, sendAt: null, operationName: "send", cancellationToken);

    public Task<SentMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default) =>
        // Scheduling goes through the messaging service (Twilio requires it for scheduled sends).
        CreateMessageAsync(toNumber, body, from: null, messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, operationName: "schedule", cancellationToken);

    private async Task<SentMessage> CreateMessageAsync(string toNumber, string body, string? from, string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, string operationName, CancellationToken cancellationToken)
    {
        try
        {
            ApiV2010AccountMessage message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
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
                ct: cancellationToken);

            if (string.IsNullOrEmpty(message.Sid))
                throw new SmsNotificationException("The provider accepted the message but returned no identifier.");

            return new SentMessage(message.Sid!, StatusValue(message.Status));
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, operationName);
        }
        catch (JsonException ex)
        {
            throw new SmsNotificationException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsNotificationException("The SMS provider is currently unreachable.", null, ex);
        }
    }

    public async Task<MessageDeliveryState> GetDeliveryStateAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            ApiV2010AccountMessage message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: cancellationToken);

            return new MessageDeliveryState(StatusValue(message.Status), message.ErrorCode, message.ErrorMessage);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "status read");
        }
        catch (JsonException ex)
        {
            throw new SmsNotificationException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsNotificationException("The SMS provider is currently unreachable.", null, ex);
        }
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "cancel");
        }
        catch (JsonException ex)
        {
            throw new SmsNotificationException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsNotificationException("The SMS provider is currently unreachable.", null, ex);
        }
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            // Setting the body to empty is Twilio's redaction path: the record and final status survive,
            // only the body text becomes non-retrievable. (This is an update, not a delete.)
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "content disposal");
        }
        catch (JsonException ex)
        {
            throw new SmsNotificationException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsNotificationException("The SMS provider is currently unreachable.", null, ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        int? page = null;
        string? pageToken = null;
        var pages = 0;

        try
        {
            while (true)
            {
                ListMessageResponse response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,          // server-side From filter — this app's number only
                    dateSent: null,
                    dateSentQuery: to,                    // DateSent<  (upper bound)
                    dateSentQueryQuery: from,             // DateSent>  (lower bound)
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: cancellationToken);

                if (response.Messages is not null)
                {
                    foreach (var message in response.Messages)
                        results.Add(ToProviderMessage(message));
                }

                if (++pages >= MaxReconciliationPages)
                    break;

                var next = ParseNextPage(response.NextPageUri);
                if (next is null)
                    break;

                page = next.Value.Page;
                pageToken = next.Value.PageToken;
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex, "reconciliation listing");
        }
        catch (JsonException ex)
        {
            throw new SmsNotificationException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsNotificationException("The SMS provider is currently unreachable.", null, ex);
        }

        return results;
    }

    // --- helpers -------------------------------------------------------------------------------

    private static ProviderMessage ToProviderMessage(ApiV2010AccountMessage message)
    {
        DateTimeOffset? dateSent = null;
        if (!string.IsNullOrEmpty(message.DateSent) && DateTimeOffset.TryParse(message.DateSent, out var parsed))
            dateSent = parsed;

        return new ProviderMessage(message.Sid, message.From, message.To, StatusValue(message.Status), dateSent, message.Body);
    }

    private static string StatusValue(MessageEnumStatus? status) => status?.Value ?? "unknown";

    private static string DescribeValidationErrors(LookupResponse response)
    {
        if (response.ValidationErrors is { Count: > 0 })
        {
            var reasons = new List<string>();
            foreach (var error in response.ValidationErrors)
                reasons.Add(error.Value);
            return "The number is not a valid destination: " + string.Join(", ", reasons) + ".";
        }

        return "The number is not a valid destination.";
    }

    private static (int? Page, string? PageToken)? ParseNextPage(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
            return null;

        var queryIndex = nextPageUri!.IndexOf('?');
        if (queryIndex < 0 || queryIndex == nextPageUri.Length - 1)
            return null;

        int? page = null;
        string? pageToken = null;
        var query = nextPageUri[(queryIndex + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                continue;

            var key = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);

            if (string.Equals(key, "Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var parsedPage))
                page = parsedPage;
            else if (string.Equals(key, "PageToken", StringComparison.OrdinalIgnoreCase))
                pageToken = value;
        }

        return page is null && pageToken is null ? null : (page, pageToken);
    }

    /// <summary>
    /// Translates an SDK error into a caller-safe exception. Only the provider's HTTP status and
    /// numeric error code are carried — never the provider's free-text body, which can echo the
    /// destination number.
    /// </summary>
    private static SmsNotificationException ToProviderException(SdkException<RawError> ex, string operationName)
    {
        HttpStatusCode status = ex.Error.StatusCode;
        int? code = TryReadProviderErrorCode(ex.Error);
        var suffix = code is null ? $"status {(int)status}" : $"status {(int)status}, code {code}";
        return new SmsNotificationException($"The SMS provider rejected the {operationName} request ({suffix}).", status, ex);
    }

    private static int? TryReadProviderErrorCode(RawError error)
    {
        try
        {
            var body = error.ReadAsString();
            if (string.IsNullOrWhiteSpace(body))
                return null;

            var parsed = JsonSerializer.Deserialize<TwilioErrorBody>(body, ErrorJsonOptions);
            return parsed?.Code;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A malformed/non-JSON error body must never throw past this boundary.
            return null;
        }
    }

    private sealed record TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }
    }
}
