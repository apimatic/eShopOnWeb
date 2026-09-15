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
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// The Twilio-backed implementation of <see cref="ISmsGateway"/>. It is the ONE place the Twilio SDK is
/// touched: every SDK/transport failure is translated here into a single <see cref="SmsGatewayException"/>
/// carrying the provider's HTTP status, and no provider SDK type crosses back to callers. The configured
/// sending number and messaging service are applied here so callers never pass account details.
///
/// A shopper's number is never written to a log line from this class.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const long ListPageSize = 100;
    private const int MaxReconciliationPages = 500;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default)
    {
        LookupResponse response;
        try
        {
            // Lookups is served from a different host (Default4) than messaging; the SDK selects it for us.
            // 15 optional query params after the number are nullable-no-default, so each is passed as null.
            response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                rawNumber, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            // A number the provider cannot parse/find is a rejection, not an outage.
            if (status == (int)HttpStatusCode.NotFound || (status >= 400 && status < 500 && status is not 408 and not 429))
            {
                _logger.LogInformation("Twilio lookup rejected a number (HTTP {Status}).", status);
                return PhoneValidationResult.Invalid($"The provider could not validate the number (HTTP {status}).");
            }

            throw Translate(ex, "lookup");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The provider returned a lookup response that could not be processed.", SmsGatewayErrorKind.Unknown, null, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The provider could not be reached to validate the number.", SmsGatewayErrorKind.Transient, null, ex);
        }

        if (response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber))
        {
            return PhoneValidationResult.Valid(response.PhoneNumber!);
        }

        var reason = DescribeValidationErrors(response.ValidationErrors);
        return PhoneValidationResult.Invalid(reason);
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        var message = await InvokeAsync(
            token => SendOnceAsync(() => CreateMessageAsync(
                to: toE164, body: body, from: _settings.FromNumber,
                messagingServiceSid: null, scheduleType: null, sendAt: null, ct: token)),
            "send message", ct);

        return new SmsSendResult(
            RequireSid(message),
            StatusValue(message.Status),
            message.ErrorCode,
            message.ErrorMessage);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        // Provider-side scheduling is Messaging-Services only: supply the messaging service, not a From number.
        var message = await InvokeAsync(
            token => SendOnceAsync(() => CreateMessageAsync(
                to: toE164, body: body, from: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, ct: token)),
            "schedule message", ct);

        return new SmsSendResult(
            RequireSid(message),
            StatusValue(message.Status),
            message.ErrorCode,
            message.ErrorMessage,
            sendAt);
    }

    public async Task<SmsMessageState> CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default)
    {
        var message = await InvokeAsync(
            token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: token),
            "cancel scheduled message", ct);

        return new SmsMessageState(StatusValue(message.Status), message.ErrorCode, message.ErrorMessage);
    }

    public async Task<SmsMessageState> FetchStatusAsync(string providerMessageSid, CancellationToken ct = default)
    {
        var message = await InvokeAsync(
            token => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: token),
            "fetch message", ct);

        return new SmsMessageState(StatusValue(message.Status), message.ErrorCode, message.ErrorMessage);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken ct = default)
    {
        // Redaction is an in-place UpdateMessage with an empty body — the record and final status survive,
        // only the body text is cleared at the provider. (DeleteMessage would remove the whole record.)
        var message = await InvokeAsync(
            token => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: token),
            "redact message body", ct);

        if (!string.IsNullOrEmpty(message.Body))
        {
            throw new SmsGatewayException("The provider did not clear the message body.", SmsGatewayErrorKind.Unknown);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var records = new List<ProviderMessageRecord>();
        int? page = null;
        string? pageToken = null;
        var pages = 0;

        while (true)
        {
            var response = await InvokeAsync(
                token => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,          // server-side sender filter — this app's number only
                    dateSent: null,
                    dateSentQuery: to,                    // wire DateSent< — upper bound
                    dateSentQueryQuery: from,             // wire DateSent> — lower bound
                    pageSize: ListPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: token),
                "list messages", ct);

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    records.Add(ToRecord(message));
                }
            }

            if (++pages >= MaxReconciliationPages)
            {
                _logger.LogWarning("Reconciliation stopped at the {MaxPages}-page cap; the range may not be fully covered.", MaxReconciliationPages);
                break;
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break; // provider signalled the end
            }

            var (nextPage, nextToken) = ParseNextPage(response.NextPageUri!);
            if (nextPage is null && nextToken is null)
            {
                break; // cannot advance — stop rather than loop forever
            }

            page = nextPage;
            pageToken = nextToken;
        }

        return records;
    }

    // --- SDK plumbing -------------------------------------------------------------------------------

    private Task<ApiV2010AccountMessage> CreateMessageAsync(
        string to, string body, string? from, string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, CancellationToken ct)
    {
        // Named arguments: the create-message operation has many nullable-no-default params that must each
        // be passed. Only the sender (from OR messagingServiceSid), body, and schedule fields are set.
        return _client.Api20100401Message.CreateMessage(
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
    }

    /// <summary>Run a create-message call under the single-send guard so a transport retry cannot duplicate it.</summary>
    private static async Task<ApiV2010AccountMessage> SendOnceAsync(Func<Task<ApiV2010AccountMessage>> create)
    {
        using (SingleSendGuard.Begin())
        {
            return await create();
        }
    }

    /// <summary>Wrap an SDK call, translating every provider/transport failure into a single domain exception.</summary>
    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, string operation, CancellationToken ct)
    {
        try
        {
            return await call(ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, operation);
        }
        catch (DuplicateSendBlockedException ex)
        {
            // The first attempt failed on the wire and the guard refused the retry — the message may or may
            // not have reached the provider, so the outcome is genuinely unknown, not a clean failure.
            _logger.LogWarning("A {Operation} retry was blocked to avoid a duplicate send; outcome is unknown.", operation);
            throw new SmsGatewayException($"The outcome of {operation} is unknown (a duplicate send was prevented).", SmsGatewayErrorKind.Unknown, null, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("The provider returned an unreadable response for {Operation}.", operation);
            throw new SmsGatewayException($"The provider returned a response for {operation} that could not be processed.", SmsGatewayErrorKind.Unknown, null, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("The provider could not be reached for {Operation}.", operation);
            throw new SmsGatewayException($"The provider could not be reached for {operation}.", SmsGatewayErrorKind.Transient, null, ex);
        }
    }

    private SmsGatewayException Translate(SdkException<RawError> ex, string operation)
    {
        var status = (int)ex.Error.StatusCode;
        var providerCode = TryReadProviderErrorCode(ex.Error);

        // 4xx (other than throttling/timeout) is a permanent rejection; 408/429/5xx is transient.
        var kind = status is 408 or 429 || status >= 500
            ? SmsGatewayErrorKind.Transient
            : SmsGatewayErrorKind.Rejected;

        // Log only the HTTP status and the provider's numeric error code — never the raw body, which can
        // carry the destination number for an invalid-'To' error.
        _logger.LogWarning("Twilio {Operation} failed: HTTP {Status}, provider code {Code}.", operation, status, providerCode);

        var suffix = providerCode is null ? string.Empty : $", provider code {providerCode}";
        return new SmsGatewayException($"The provider rejected the {operation} request (HTTP {status}{suffix}).", kind, status, ex);
    }

    private static int? TryReadProviderErrorCode(RawError error)
    {
        try
        {
            var body = error.ReadAsJson<TwilioErrorBody>();
            return body?.Code;
        }
        catch (JsonException)
        {
            return null; // a Case-B error body is not guaranteed to be JSON
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ProviderMessageRecord ToRecord(ApiV2010AccountMessage message)
    {
        DateTimeOffset? dateSent = null;
        if (!string.IsNullOrWhiteSpace(message.DateSent) && DateTimeOffset.TryParse(message.DateSent, out var parsed))
        {
            dateSent = parsed;
        }

        return new ProviderMessageRecord(
            sid: message.Sid ?? string.Empty,
            status: StatusValue(message.Status),
            to: message.To,
            from: message.From,
            dateSent: dateSent,
            errorCode: message.ErrorCode,
            errorMessage: message.ErrorMessage);
    }

    private static string RequireSid(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            throw new SmsGatewayException("The provider accepted the message but returned no identifier.", SmsGatewayErrorKind.Unknown);
        }

        return message.Sid!;
    }

    private static string? StatusValue(MessageEnumStatus? status) => status?.Value;

    private static string? DescribeValidationErrors(IReadOnlyList<ValidationError>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return "The provider does not consider the number a usable destination.";
        }

        var reasons = new List<string>(errors.Count);
        foreach (var error in errors)
        {
            reasons.Add(error.Value);
        }

        return "The provider rejected the number: " + string.Join(", ", reasons) + ".";
    }

    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0 || queryStart == nextPageUri.Length - 1)
        {
            return (null, null);
        }

        int? page = null;
        string? pageToken = null;

        var query = nextPageUri.Substring(queryStart + 1);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = pair.Substring(0, eq);
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));

            if (string.Equals(name, "Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
            {
                page = p;
            }
            else if (string.Equals(name, "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
            }
        }

        return (page, pageToken);
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("more_info")]
        public string? MoreInfo { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }
    }
}
