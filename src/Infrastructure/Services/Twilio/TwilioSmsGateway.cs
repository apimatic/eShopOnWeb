using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Api;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio-backed implementation of <see cref="ISmsGateway"/>. All Twilio interaction goes
/// through the AsadAli.TwilioSdk client. The messaging calls honour the optional
/// <c>Twilio:BaseUrl</c> override; number validation uses Twilio Lookups (a separate host that
/// the override does not govern).
/// </summary>
public sealed class TwilioSmsGateway : ISmsGateway, IDisposable
{
    private readonly TwilioSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly TwilioSdkClient _client;

    public TwilioSmsGateway(IOptions<TwilioSettings> settings)
    {
        _settings = settings.Value;
        _httpClient = new HttpClient();

        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = _settings.AccountSid,
                Password = _settings.AuthToken
            }
        };

        // Apply the messaging-API base-URL override verbatim when configured; leave the SDK
        // default otherwise. This reaches the api.twilio.com surface (all Message calls); the
        // Lookups host is a different server and is intentionally unaffected.
        if (_settings.HasBaseUrlOverride)
        {
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl!;
        }

        _client = new TwilioSdkClient(_httpClient, options);
    }

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            LookupResponse response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null, partnerSubId: null,
                requestOptions: null, ct: cancellationToken);

            if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
            {
                return new PhoneValidationResult(true, response.PhoneNumber, null);
            }

            return new PhoneValidationResult(false, null, "The provider does not consider this a valid, reachable number.");
        }
        catch (SdkException<RawError> ex)
        {
            // A number the provider cannot even look up (e.g. malformed) is not a usable destination.
            var (_, message) = ReadError(ex);
            return new PhoneValidationResult(false, null, message ?? "The provider could not validate this number.");
        }
    }

    public async Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var message = await CreateMessageAsync(
            to: toE164, body: body,
            from: _settings.FromNumber, messagingServiceSid: null,
            scheduleType: null, sendAt: null,
            cancellationToken: cancellationToken);
        return ToDispatchResult(message);
    }

    public async Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default)
    {
        // Scheduling requires the messaging service; the sender is drawn from that service.
        var message = await CreateMessageAsync(
            to: toE164, body: body,
            from: null, messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAtUtc,
            cancellationToken: cancellationToken);
        return ToDispatchResult(message);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        await _client.Api20100401Message.UpdateMessage(
            _settings.AccountSid, providerMessageSid,
            body: null, status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null, ct: cancellationToken);
    }

    public async Task<SmsDeliveryState?> FetchStateAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var message = await _client.Api20100401Message.FetchMessage(
            _settings.AccountSid, providerMessageSid,
            requestOptions: null, ct: cancellationToken);
        if (message is null)
        {
            return null;
        }
        return new SmsDeliveryState(StatusToString(message.Status), message.ErrorCode, message.ErrorMessage);
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Empty body redacts the message text at the provider while the record (sid, status) survives.
        await _client.Api20100401Message.UpdateMessage(
            _settings.AccountSid, providerMessageSid,
            body: string.Empty, status: null,
            requestOptions: null, ct: cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListOutboundAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessageRecord>();
        int page = 0;
        const int pageSize = 1000;
        const int maxPages = 100; // safety bound

        while (page < maxPages)
        {
            // From filter applied server-side; date bounds: dateSentQueryQuery = DateSent> (lower),
            // dateSentQuery = DateSent< (upper).
            ListMessageResponse response = await _client.Api20100401Message.ListMessage(
                _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: toUtc,
                dateSentQueryQuery: fromUtc,
                pageSize: pageSize,
                page: page,
                pageToken: null,
                requestOptions: null,
                ct: cancellationToken);

            var messages = response?.Messages;
            if (messages is null || messages.Count == 0)
            {
                break;
            }

            foreach (var message in messages)
            {
                if (string.IsNullOrEmpty(message.Sid))
                {
                    continue;
                }
                results.Add(new ProviderMessageRecord(
                    message.Sid!,
                    StatusToString(message.Status),
                    message.To,
                    message.From,
                    ParseProviderDate(message.DateSent),
                    message.ErrorCode));
            }

            if (string.IsNullOrEmpty(response!.NextPageUri))
            {
                break;
            }
            page++;
        }

        return results;
    }

    private async Task<ApiV2010AccountMessage> CreateMessageAsync(
        string to, string body, string? from, string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        return await _client.Api20100401Message.CreateMessage(
            _settings.AccountSid,
            to,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null,
            contentRetention: null, addressRetention: null, smartEncoded: null,
            persistentAction: null, trafficType: null, shortenUrls: null,
            scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null,
            contentVariables: null, riskCheck: null,
            from: from, fallbackFrom: null, messagingServiceSid: messagingServiceSid, body: body,
            mediaUrl: null, contentSid: null,
            requestOptions: null, ct: cancellationToken);
    }

    private static SmsDispatchResult ToDispatchResult(ApiV2010AccountMessage message) =>
        new(message.Sid, StatusToString(message.Status), message.ErrorCode, message.ErrorMessage);

    private static string? StatusToString(MessageEnumStatus? status)
    {
        if (status is null)
        {
            return null;
        }

        // The SDK enum is a StringEnum wrapper whose ToString() renders as
        // "MessageEnumStatus { Value = delivered }"; extract the provider's own wire value.
        var text = status.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        const string marker = "Value = ";
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var value = text.Substring(idx + marker.Length).TrimEnd('}', ' ').Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }
        return text;
    }

    private static DateTimeOffset? ParseProviderDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static (HttpStatusCode? status, string? message) ReadError(SdkException<RawError> ex)
    {
        try
        {
            var status = ex.Error.StatusCode;
            try
            {
                var body = ex.Error.ReadAsJson<TwilioApiError>();
                if (body is not null && !string.IsNullOrEmpty(body.Message))
                {
                    return (status, body.Message);
                }
            }
            catch
            {
                // Error body was not the expected shape; fall back to status only.
            }
            return (status, null);
        }
        catch
        {
            return (null, null);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    /// <summary>Minimal shape of Twilio's JSON error body (code/message live in the body, not typed).</summary>
    private sealed class TwilioApiError
    {
        public int? Code { get; set; }
        public string? Message { get; set; }
    }
}
