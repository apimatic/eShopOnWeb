using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio-backed implementation of <see cref="IMessagingProvider"/>. Every Twilio interaction goes through
/// the APIMatic-generated <see cref="TwilioSdkClient"/>. All message operations throw
/// <c>SdkException&lt;RawError&gt;</c> on a non-2xx status; those, transport failures and malformed 2xx
/// bodies are translated into <see cref="MessagingProviderException"/> at this boundary so the rest of the
/// application has a single provider-failure type to handle. Phone numbers and the auth token are never logged.
/// </summary>
public class TwilioMessagingProvider : IMessagingProvider
{
    /// <summary>Whole-call budget for a single provider call (bounds retries + backoff, unlike the per-attempt timeouts).</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    private const int ReconciliationPageSize = 1000;
    private const int MaxReconciliationPages = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            // Lookup V2 rides the lookups host (Default4), not the messaging Twilio:BaseUrl override.
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
                ct: cts.Token);

            if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
            {
                // Store the provider's own canonical (E.164) form, not whatever the caller typed.
                return PhoneValidationResult.Valid(response.PhoneNumber);
            }

            return PhoneValidationResult.Invalid(DescribeValidationErrors(response.ValidationErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            // A number the provider can't parse or reach comes back as a client error — that's a rejection,
            // not an outage. Auth/quota/server errors are genuine provider failures.
            if (status is 404 or 400)
            {
                return PhoneValidationResult.Invalid("The phone number is not a valid, reachable SMS destination.");
            }
            throw Translate(ex, "phone number lookup");
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The messaging provider returned a lookup response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new MessagingProviderException("The messaging provider was unreachable.", innerException: ex);
        }
    }

    public async Task<SentMessage> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken)
    {
        // Immediate send FROM our configured number, so the message is counted during reconciliation.
        var response = await InvokeAsync(
            ct => CreateMessage(toE164, body, from: _settings.FromNumber, messagingServiceSid: null, scheduleType: null, sendAt: null, ct),
            "message send", cancellationToken);
        return ToSentMessage(response);
    }

    public async Task<SentMessage> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken)
    {
        // Scheduling requires a Messaging Service as the sender; the provider holds the message until sendAt.
        var response = await InvokeAsync(
            ct => CreateMessage(toE164, body, from: null, messagingServiceSid: _settings.MessagingServiceSid, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAtUtc, ct),
            "message schedule", cancellationToken);
        return ToSentMessage(response);
    }

    public async Task<MessageDeliveryStatus> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var response = await InvokeAsync(
            ct => _client.Api20100401Message.UpdateMessage(_settings.AccountSid, providerMessageSid, body: null, status: MessageEnumUpdateStatus.Canceled, ct: ct),
            "message cancel", cancellationToken);
        return new MessageDeliveryStatus(response.Status?.Value ?? NotificationDeliveryState.Canceled, response.ErrorCode, response.ErrorMessage);
    }

    public async Task<MessageDeliveryStatus> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var response = await InvokeAsync(
            ct => _client.Api20100401Message.FetchMessage(_settings.AccountSid, providerMessageSid, ct: ct),
            "message fetch", cancellationToken);
        return new MessageDeliveryStatus(response.Status?.Value ?? "unknown", response.ErrorCode, response.ErrorMessage);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        // Updating the body to empty redacts the stored text at the provider while the record + status survive.
        _ = await InvokeAsync(
            ct => _client.Api20100401Message.UpdateMessage(_settings.AccountSid, providerMessageSid, body: string.Empty, status: null, ct: ct),
            "message content disposal", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var results = new List<ProviderMessage>();
        string? pageToken = null;
        int? page = null;
        var pages = 0;

        while (true)
        {
            // Filter server-side by sender + range. Wire-name trap: dateSentQuery is the UPPER bound (DateSent<=),
            // dateSentQueryQuery is the LOWER bound (DateSent>=).
            var response = await InvokeAsync(
                ct => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: fromNumber,
                    dateSent: null,
                    dateSentQuery: toUtc,
                    dateSentQueryQuery: fromUtc,
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: ct),
                "message list", cancellationToken);

            if (response.Messages is not null)
            {
                results.AddRange(response.Messages.Select(ToProviderMessage));
            }

            if (++pages >= MaxReconciliationPages)
            {
                break; // page-cap backstop; the range is far larger than any real reconciliation window at this volume
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }

            pageToken = GetQueryParameter(response.NextPageUri, "PageToken");
            page = int.TryParse(GetQueryParameter(response.NextPageUri, "Page"), out var next) ? next : null;
            if (string.IsNullOrEmpty(pageToken) && page is null)
            {
                break;
            }
        }

        return results;
    }

    // ----- SDK plumbing -----

    private Task<ApiV2010AccountMessage> CreateMessage(
        string to, string body, string? from, string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, CancellationToken ct)
        => _client.Api20100401Message.CreateMessage(
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
            ct: ct);

    private static SentMessage ToSentMessage(ApiV2010AccountMessage response)
    {
        if (string.IsNullOrEmpty(response.Sid))
        {
            throw new MessagingProviderException("The messaging provider accepted the request but returned no message identifier.");
        }
        return new SentMessage(response.Sid, response.Status?.Value ?? NotificationDeliveryState.Queued);
    }

    private static ProviderMessage ToProviderMessage(ApiV2010AccountMessage message)
    {
        DateTimeOffset? dateSent = DateTimeOffset.TryParse(message.DateSent, out var parsed) ? parsed : null;
        return new ProviderMessage(message.Sid, message.Status?.Value, message.From, message.To, dateSent);
    }

    /// <summary>
    /// Runs a provider call under a whole-call timeout and translates every failure mode into
    /// <see cref="MessagingProviderException"/>: provider (non-2xx) errors, transport failures, and a
    /// malformed 2xx body (a <see cref="JsonException"/> that a status-only catch would miss). A cancel
    /// the caller actually requested is left to propagate.
    /// </summary>
    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, string action, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, action);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException($"The messaging provider returned a {action} response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new MessagingProviderException("The messaging provider was unreachable.", innerException: ex);
        }
    }

    private static MessagingProviderException Translate(SdkException<RawError> ex, string action)
    {
        var raw = ex.Error;
        int? code = null;
        string? message = null;
        try
        {
            var body = raw.ReadAsJson<TwilioErrorBody>();
            code = body?.Code;
            message = body?.Message;
        }
        catch (Exception)
        {
            // The error body was not the JSON shape we expected; the HTTP status is still carried below.
        }

        return new MessagingProviderException($"The messaging provider rejected the {action} request.", raw.StatusCode, code, message, ex);
    }

    private static string DescribeValidationErrors(IReadOnlyList<ValidationError>? validationErrors)
    {
        if (validationErrors is null || validationErrors.Count == 0)
        {
            return "The phone number is not a usable SMS destination.";
        }
        var reasons = string.Join(", ", validationErrors.Select(v => v.Value));
        return $"The phone number is not a usable SMS destination ({reasons}).";
    }

    private static string? GetQueryParameter(string url, string name)
    {
        var queryStart = url.IndexOf('?');
        if (queryStart < 0)
        {
            return null;
        }

        foreach (var pair in url[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null;
    }

    private sealed record TwilioErrorBody
    {
        [JsonPropertyName("code")] public int? Code { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
        [JsonPropertyName("more_info")] public string? MoreInfo { get; init; }
    }
}
