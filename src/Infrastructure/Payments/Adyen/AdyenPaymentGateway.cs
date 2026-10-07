using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AdyenApIs;
using AdyenApIs.Core;
using AdyenApIs.Core.Exceptions;
using AdyenApIs.Core.Hooks;
using AdyenApIs.Errors;
using AdyenApIs.Models;
using AdyenApIs.Models.AnyOf;
using AdyenApIs.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

/// <summary>
/// The one place the application talks to Adyen. Every SDK failure is translated here into a result the
/// caller records; nothing provider-specific escapes. Writes are sent at most twice: once, and — only when
/// the first send's outcome is unknown and time remains — once more with the same idempotency key to settle it.
/// </summary>
public sealed class AdyenPaymentGateway : IPaymentGateway
{
    private const int MaxRecordedBodyChars = 256 * 1024;

    private readonly AdyenApIsClient _client;
    private readonly AdyenSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AdyenPaymentGateway> _logger;

    public AdyenPaymentGateway(AdyenApIsClient client, IOptions<AdyenSettings> settings, TimeProvider timeProvider,
        ILogger<AdyenPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Currency => _settings.Currency.Trim().ToUpperInvariant();

    public async Task<PaymentAuthorisationResult> AuthoriseAsync(PaymentAuthorisationRequest request, CancellationToken cancellationToken)
    {
        var recorder = new ExchangeRecorder(_timeProvider);
        var sdkRequest = new AdyenApIs.Requests.Payments.CreatePaymentRequest
        {
            IdempotencyKey = request.IdempotencyKey,
            Body = new PaymentRequest
            {
                Amount = new Amount2 { Currency = request.Currency, Value = request.AmountMinor },
                MerchantAccount = _settings.MerchantAccount,
                PaymentMethod = PaymentMethod111.Card(new Card
                {
                    Type = Type12.Scheme,
                    EncryptedCardNumber = request.Card.EncryptedCardNumber,
                    EncryptedExpiryMonth = request.Card.EncryptedExpiryMonth,
                    EncryptedExpiryYear = request.Card.EncryptedExpiryYear,
                    EncryptedSecurityCode = request.Card.EncryptedSecurityCode,
                    HolderName = request.Card.HolderName,
                }),
                Reference = request.Reference,
                MerchantOrderReference = request.MerchantOrderReference,
                ReturnUrl = request.ReturnUrl,
                // Take the money now: capture immediately after authorisation.
                CaptureDelayHours = 0,
                ShopperInteraction = ShopperInteraction.Ecommerce,
            }
        };
        var requestOptions = new RequestOptions { Hooks = [recorder.Hook] };

        for (var send = 1; ; send++)
        {
            try
            {
                var response = await _client.Payments.CreatePayment(sdkRequest, requestOptions, cancellationToken);
                return MapPayment(response, request, recorder.Exchanges);
            }
            catch (ApiException<CreatePaymentError> ex)
            {
                ex.Error.TryGetServiceError(out var serviceError);
                LogProviderError("CreatePayment", request.Reference, ex.StatusCode, serviceError);
                return PaymentFailure(ex.StatusCode, serviceError, recorder.Exchanges);
            }
            catch (ResponseDeserializationException ex)
            {
                // The raw body is kept by the recorder either way; only our typed view of it failed.
                _logger.LogError(ex, "Adyen CreatePayment for {Reference} returned HTTP {Status} with a body that could not be read.",
                    request.Reference, (int)ex.StatusCode);
                // A success we cannot read may still have charged the card: settle it later, never report a failure.
                return IsSuccess(ex.StatusCode)
                    ? UnknownPayment(timedOut: false, recorder.Exchanges)
                    : PaymentFailure(ex.StatusCode, null, recorder.Exchanges);
            }
            catch (AuthSchemeException ex)
            {
                _logger.LogError(ex, "Adyen credentials could not be applied; CreatePayment for {Reference} was not sent.", request.Reference);
                return new PaymentAuthorisationResult(PaymentAuthorisationOutcome.ProviderError, null, null, null, null, null,
                    "credentials", "Adyen credentials could not be applied.", false, recorder.Exchanges);
            }
            catch (SdkConnectionException ex) when (send == 1 && !cancellationToken.IsCancellationRequested)
            {
                // The payment may have landed. Re-send with the same idempotency key so Adyen returns the
                // original result instead of charging again.
                _logger.LogWarning("Adyen CreatePayment for {Reference} got no usable answer ({Error}); re-sending with the same idempotency key to settle it.",
                    request.Reference, ex.GetType().Name);
            }
            catch (SdkConnectionException ex)
            {
                _logger.LogError("Adyen CreatePayment for {Reference} still has no answer ({Error}); outcome recorded as unknown.",
                    request.Reference, ex.GetType().Name);
                return UnknownPayment(timedOut: true, recorder.Exchanges);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Adyen CreatePayment for {Reference} exceeded the time budget; outcome recorded as unknown.", request.Reference);
                return UnknownPayment(timedOut: true, recorder.Exchanges);
            }
        }
    }

    public async Task<RefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken)
    {
        var recorder = new ExchangeRecorder(_timeProvider);
        var sdkRequest = new AdyenApIs.Requests.Modifications.RefundPaymentRequest
        {
            PaymentPspReference = request.PaymentPspReference,
            IdempotencyKey = request.IdempotencyKey,
            Body = new PaymentRefundRequest
            {
                Amount = new Amount37 { Currency = request.Currency, Value = request.AmountMinor },
                MerchantAccount = _settings.MerchantAccount,
                Reference = request.Reference,
            }
        };
        var requestOptions = new RequestOptions { Hooks = [recorder.Hook] };

        for (var send = 1; ; send++)
        {
            try
            {
                var response = await _client.Modifications.RefundPayment(sdkRequest, requestOptions, cancellationToken);
                return new RefundResult(RefundOutcome.Received, response.PspReference, null, null, false, recorder.Exchanges);
            }
            catch (ApiException<RefundPaymentError> ex)
            {
                ex.Error.TryGetServiceError(out var serviceError);
                LogProviderError("RefundPayment", request.Reference, ex.StatusCode, serviceError);
                return RefundFailure(ex.StatusCode, serviceError, recorder.Exchanges);
            }
            catch (ResponseDeserializationException ex)
            {
                _logger.LogError(ex, "Adyen RefundPayment for {Reference} returned HTTP {Status} with a body that could not be read.",
                    request.Reference, (int)ex.StatusCode);
                return IsSuccess(ex.StatusCode)
                    ? new RefundResult(RefundOutcome.Unknown, null, null, null, false, recorder.Exchanges)
                    : RefundFailure(ex.StatusCode, null, recorder.Exchanges);
            }
            catch (AuthSchemeException ex)
            {
                _logger.LogError(ex, "Adyen credentials could not be applied; RefundPayment for {Reference} was not sent.", request.Reference);
                return new RefundResult(RefundOutcome.ProviderError, null, "credentials", "Adyen credentials could not be applied.", false, recorder.Exchanges);
            }
            catch (SdkConnectionException ex) when (send == 1 && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Adyen RefundPayment for {Reference} got no usable answer ({Error}); re-sending with the same idempotency key to settle it.",
                    request.Reference, ex.GetType().Name);
            }
            catch (SdkConnectionException ex)
            {
                _logger.LogError("Adyen RefundPayment for {Reference} still has no answer ({Error}); outcome recorded as unknown.",
                    request.Reference, ex.GetType().Name);
                return new RefundResult(RefundOutcome.Unknown, null, null, null, true, recorder.Exchanges);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Adyen RefundPayment for {Reference} exceeded the time budget; outcome recorded as unknown.", request.Reference);
                return new RefundResult(RefundOutcome.Unknown, null, null, null, true, recorder.Exchanges);
            }
        }
    }

    private PaymentAuthorisationResult MapPayment(PaymentResponse response, PaymentAuthorisationRequest request,
        IReadOnlyList<ProviderExchange> exchanges)
    {
        var code = response.ResultCode;
        PaymentAuthorisationOutcome outcome;
        if (code == ResultCode1.Authorised)
            outcome = PaymentAuthorisationOutcome.Authorised;
        else if (code == ResultCode1.Refused || code == ResultCode1.Error || code == ResultCode1.Cancelled)
            outcome = PaymentAuthorisationOutcome.Refused;
        else if (response.Action is not null || code == ResultCode1.RedirectShopper || code == ResultCode1.IdentifyShopper
                 || code == ResultCode1.ChallengeShopper || code == ResultCode1.PresentToShopper)
            outcome = PaymentAuthorisationOutcome.ActionRequired;
        else
        {
            // Pending, Received, PartiallyAuthorised, or a code this build does not know: not final.
            // Treat as pending so no second payment is started until it is settled.
            outcome = PaymentAuthorisationOutcome.Pending;
            if (code is null || !code.IsKnownValue())
                _logger.LogWarning("Adyen returned unrecognised resultCode {ResultCode} for {Reference}.", code?.Value ?? "(none)", request.Reference);
        }

        long? authorised = null;
        if (outcome == PaymentAuthorisationOutcome.Authorised && response.Amount is { } amount
            && string.Equals(amount.Currency, request.Currency, StringComparison.OrdinalIgnoreCase))
        {
            authorised = amount.Value;
        }

        return new PaymentAuthorisationResult(outcome, response.PspReference, code?.Value, response.RefusalReason,
            response.RefusalReasonCode, authorised, null, null, false, exchanges);
    }

    private static PaymentAuthorisationResult PaymentFailure(HttpStatusCode status, ServiceError? error, IReadOnlyList<ProviderExchange> exchanges)
    {
        var outcome = Classify(status) switch
        {
            FailureKind.CallerData => PaymentAuthorisationOutcome.Rejected,
            FailureKind.OutcomeUnknown => PaymentAuthorisationOutcome.Unknown,
            _ => PaymentAuthorisationOutcome.ProviderError
        };
        return new PaymentAuthorisationResult(outcome, error?.PspReference, null, null, null, null,
            error?.ErrorCode ?? ((int)status).ToString(), error?.Message, false, exchanges);
    }

    private static RefundResult RefundFailure(HttpStatusCode status, ServiceError? error, IReadOnlyList<ProviderExchange> exchanges)
    {
        var outcome = Classify(status) switch
        {
            FailureKind.CallerData => RefundOutcome.Rejected,
            FailureKind.OutcomeUnknown => RefundOutcome.Unknown,
            _ => RefundOutcome.ProviderError
        };
        return new RefundResult(outcome, error?.PspReference, error?.ErrorCode ?? ((int)status).ToString(), error?.Message, false, exchanges);
    }

    private static PaymentAuthorisationResult UnknownPayment(bool timedOut, IReadOnlyList<ProviderExchange> exchanges) =>
        new(PaymentAuthorisationOutcome.Unknown, null, null, null, null, null, null, null, timedOut, exchanges);

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private enum FailureKind { CallerData, OurConfiguration, OutcomeUnknown }

    private static FailureKind Classify(HttpStatusCode status) => (int)status switch
    {
        // Adyen rejected the request content (e.g. card data it could not decrypt): no money moved.
        400 or 422 => FailureKind.CallerData,
        // Adyen failed while handling the request: it may have acted, so this is settled like a timeout.
        >= 500 => FailureKind.OutcomeUnknown,
        // 401/403 (our credentials), 404, 429 (our quota) and anything unmapped: not the shopper's to fix.
        _ => FailureKind.OurConfiguration
    };

    private void LogProviderError(string operation, string reference, HttpStatusCode status, ServiceError? error)
    {
        var level = (int)status is 401 or 403 or >= 500 ? LogLevel.Error : LogLevel.Warning;
        _logger.Log(level, "Adyen {Operation} for {Reference} failed with HTTP {Status}: errorCode {ErrorCode}, errorType {ErrorType}, pspReference {PspReference}.",
            operation, reference, (int)status, error?.ErrorCode ?? "-", error?.ErrorType ?? "-", error?.PspReference ?? "-");
    }

    /// <summary>
    /// Keeps every raw response body Adyen sends — including fields this SDK version does not model — so
    /// support can see exactly what Adyen returned. Runs once per HTTP attempt.
    /// </summary>
    private sealed class ExchangeRecorder
    {
        private readonly TimeProvider _timeProvider;
        private readonly List<ProviderExchange> _exchanges = new();

        public ExchangeRecorder(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            Hook = SdkHook.OnResponse(RecordAsync);
        }

        public SdkHook Hook { get; }

        public IReadOnlyList<ProviderExchange> Exchanges
        {
            get { lock (_exchanges) return _exchanges.ToArray(); }
        }

        private async ValueTask RecordAsync(HttpResponseMessage response, HookContext context, CancellationToken cancellationToken)
        {
            string body;
            try
            {
                // Buffer first so the SDK can still read the body after us.
                await response.Content.LoadIntoBufferAsync();
                body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (body.Length > MaxRecordedBodyChars)
                    body = body[..MaxRecordedBodyChars];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Observation must never fail a call that succeeded on the wire.
                body = $"<response body could not be read: {ex.GetType().Name}>";
            }

            lock (_exchanges)
                _exchanges.Add(new ProviderExchange(_timeProvider.GetUtcNow(), (int)response.StatusCode, body));
        }
    }
}
