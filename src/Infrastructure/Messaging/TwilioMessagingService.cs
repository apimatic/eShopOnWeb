using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The one place that talks to the Twilio SDK. Converts every SDK, transport and
/// deserialization failure into <see cref="ApplicationCore.Exceptions.MessagingException"/>
/// so the rest of the application has a single failure type to handle.
/// Never logs phone numbers, message bodies, or credentials.
/// </summary>
public class TwilioMessagingService : IMessagingService, IPhoneNumberValidator
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 100;
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingService(TwilioSdkClient client, TwilioSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken ct = default)
    {
        var response = await ExecuteAsync(token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
            ct: token), ct);

        if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
        {
            return new PhoneNumberValidationResult(true, response.PhoneNumber, null);
        }

        var reason = response.ValidationErrors is { Count: > 0 }
            ? string.Join(", ", response.ValidationErrors.Select(e => e.Value))
            : "the provider does not consider it a usable destination";
        return new PhoneNumberValidationResult(false, null, reason);
    }

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct = default)
    {
        using var scope = SingleFlightSendGuard.BeginScope();
        var message = await ExecuteAsync(token => _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid!,
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
            scheduleType: null,
            sendAt: null,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: null,
            fallbackFrom: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            body: body,
            mediaUrl: null,
            contentSid: null,
            requestOptions: null,
            ct: token), ct);

        return Map(message);
    }

    public async Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        using var scope = SingleFlightSendGuard.BeginScope();
        var message = await ExecuteAsync(token => _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid!,
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
            requestOptions: null,
            ct: token), ct);

        return Map(message);
    }

    public async Task CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default)
    {
        await ExecuteAsync(token => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid!,
            sid: messageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: token), ct);
    }

    public async Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken ct = default)
    {
        var message = await ExecuteAsync(token => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid!,
            sid: messageSid,
            requestOptions: null,
            ct: token), ct);

        return Map(message);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default)
    {
        await ExecuteAsync(token => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid!,
            sid: messageSid,
            body: "",
            status: null,
            requestOptions: null,
            ct: token), ct);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesFromSenderAsync(
        DateTimeOffset sentAfter, DateTimeOffset sentBefore, CancellationToken ct = default)
    {
        var results = new List<ProviderMessage>();
        var page = 0;
        string? nextPageUri;

        do
        {
            var currentPage = page;
            var response = await ExecuteAsync(token => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid!,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: sentBefore,
                dateSentQueryQuery: sentAfter,
                pageSize: ListPageSize,
                page: currentPage,
                pageToken: null,
                requestOptions: null,
                ct: token), ct);

            if (response.Messages != null)
            {
                results.AddRange(response.Messages.Select(Map));
            }

            nextPageUri = response.NextPageUri;
            page++;
        }
        while (!string.IsNullOrEmpty(nextPageUri) && page < MaxListPages);

        if (!string.IsNullOrEmpty(nextPageUri))
        {
            throw new ApplicationCore.Exceptions.MessagingException(
                $"The provider returned more than {MaxListPages} pages of messages for the requested range; narrow the range.");
        }

        return results;
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message)
    {
        return new ProviderMessage
        {
            Sid = message.Sid,
            To = message.To,
            Status = message.Status?.Value,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            DateSent = DateTimeOffset.TryParse(message.DateSent, out var dateSent) ? dateSent : null,
            Body = message.Body
        };
    }

    /// <summary>
    /// One boundary for every SDK call: a whole-call budget, plus conversion of every
    /// failure kind (provider rejection, transport failure, broken response body,
    /// blocked duplicate retry) into MessagingException.
    /// </summary>
    private static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToMessagingException(ex);
        }
        catch (DuplicateSendBlockedException ex)
        {
            throw new ApplicationCore.Exceptions.MessagingException(ex.Message, null, ex);
        }
        catch (JsonException ex)
        {
            // A 2xx whose body no longer matches the SDK model: the outcome is genuinely unknown.
            throw new ApplicationCore.Exceptions.MessagingException(
                "The messaging provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                throw; // the caller cancelled — not a provider failure
            }

            throw new ApplicationCore.Exceptions.MessagingException(
                "The messaging provider could not be reached or did not answer in time.", null, ex);
        }
    }

    private static ApplicationCore.Exceptions.MessagingException ToMessagingException(SdkException<RawError> ex)
    {
        var raw = ex.Error;
        string? detail = null;

        // Provider error bodies are unmodeled; extract best-effort, never throw while parsing.
        try
        {
            var payload = raw.ReadAsJson<TwilioErrorPayload>();
            detail = payload?.Message;
        }
        catch
        {
            try
            {
                detail = raw.ReadAsString();
            }
            catch
            {
                // leave detail null
            }
        }

        if (detail != null && detail.Length > 300)
        {
            detail = detail.Substring(0, 300);
        }

        var message = string.IsNullOrWhiteSpace(detail)
            ? $"The messaging provider rejected the request (HTTP {(int)raw.StatusCode})."
            : $"The messaging provider rejected the request (HTTP {(int)raw.StatusCode}): {detail}";

        return new ApplicationCore.Exceptions.MessagingException(message, raw.StatusCode, ex);
    }

    /// <summary>Best-effort shape of a Twilio error body (code/message/status/more_info).</summary>
    private sealed class TwilioErrorPayload
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }

        [JsonPropertyName("more_info")]
        public string? MoreInfo { get; set; }
    }
}
