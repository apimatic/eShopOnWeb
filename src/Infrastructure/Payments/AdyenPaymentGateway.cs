using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AdyenApIs;
using AdyenApIs.Core.ErrorResponse;
using AdyenApIs.Core.Exceptions;
using AdyenApIs.Errors;
using AdyenApIs.Models;
using AdyenApIs.Models.AnyOf;
using AdyenApIs.Models.Enums;
using AdyenApIs.Requests.Modifications;
using AdyenApIs.Requests.Payments;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Adyen Checkout behind <see cref="IPaymentGateway"/>. This is the only class that knows SDK types; every
/// SDK failure is translated here into a result the order flow can act on.
/// </summary>
public sealed class AdyenPaymentGateway : IPaymentGateway
{
    /// <summary>Bound on one HTTP attempt (SDK per-attempt timeout). Two attempts fit inside the request budget.</summary>
    public static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Backstop on one HTTP attempt at the transport layer.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(12);

    private readonly AdyenApIsClient _client;
    private readonly AdyenSettings _settings;
    private readonly ILogger<AdyenPaymentGateway> _logger;

    public AdyenPaymentGateway(AdyenApIsClient client, IOptions<AdyenSettings> settings, ILogger<AdyenPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.Currency!.Trim();

    private string MerchantAccount => _settings.MerchantAccount!.Trim();

    public async Task<ChargeResult> ChargeAsync(ChargeCommand command, CancellationToken cancellationToken)
    {
        var request = new CreatePaymentRequest
        {
            IdempotencyKey = command.IdempotencyKey,
            Body = new PaymentRequest
            {
                Amount = new Amount2 { Currency = command.Currency, Value = command.AmountMinorUnits },
                MerchantAccount = MerchantAccount,
                PaymentMethod = PaymentMethod111.Card(new Card
                {
                    Type = Type12.Scheme,
                    EncryptedCardNumber = command.Card.EncryptedCardNumber,
                    EncryptedExpiryMonth = command.Card.EncryptedExpiryMonth,
                    EncryptedExpiryYear = command.Card.EncryptedExpiryYear,
                    EncryptedSecurityCode = command.Card.EncryptedSecurityCode,
                    HolderName = command.Card.HolderName,
                }),
                Reference = command.MerchantReference,
                ReturnUrl = command.ReturnUrl,
                // Take the money now: capture immediately after authorisation.
                CaptureDelayHours = 0,
                ShopperInteraction = ShopperInteraction.Ecommerce,
                Channel = Channel2.Web,
            }
        };

        var sent = await SendSettlingAsync(token => SendCreatePaymentAsync(request, token), "CreatePayment", command.MerchantReference, cancellationToken);
        if (sent.Response is null)
        {
            return sent.Failure switch
            {
                SendFailure.Rejected => new ChargeResult(ChargeStatus.Invalid,
                    $"The card details were rejected{Detail(sent.ProviderMessage)}. Check the card number, expiry date and security code, or use a different card. No money was taken."),
                SendFailure.MerchantSide => new ChargeResult(ChargeStatus.ProviderError,
                    "The payment service is unavailable right now. No money was taken; please try again later."),
                _ => new ChargeResult(ChargeStatus.Unknown, UnknownChargeMessage(sent.TimedOut), TimedOut: sent.TimedOut)
            };
        }

        return await InterpretPaymentAsync(sent.Response, command, cancellationToken);
    }

    private async Task<ChargeResult> InterpretPaymentAsync(PaymentResponse response, ChargeCommand command, CancellationToken cancellationToken)
    {
        var psp = response.PspReference;
        var code = response.ResultCode?.Value;
        var reason = response.RefusalReason;
        var reasonCode = response.RefusalReasonCode;

        var kind = response.ResultCode is null
            ? PaymentResultKind.NotFinal
            : response.ResultCode.Match(
                onAuthenticationFinished: () => PaymentResultKind.Unexpected,
                onAuthenticationNotRequired: () => PaymentResultKind.Unexpected,
                onAuthorised: () => PaymentResultKind.Authorised,
                onCancelled: () => PaymentResultKind.Cancelled,
                onChallengeShopper: () => PaymentResultKind.ShopperActionRequired,
                onError: () => PaymentResultKind.Error,
                onIdentifyShopper: () => PaymentResultKind.ShopperActionRequired,
                onPartiallyAuthorised: () => PaymentResultKind.PartiallyAuthorised,
                onPending: () => PaymentResultKind.NotFinal,
                onPresentToShopper: () => PaymentResultKind.ShopperActionRequired,
                onReceived: () => PaymentResultKind.NotFinal,
                onRedirectShopper: () => PaymentResultKind.ShopperActionRequired,
                onRefused: () => PaymentResultKind.Refused,
                onSuccess: () => PaymentResultKind.Unexpected,
                otherwise: _ => PaymentResultKind.Unexpected);

        if (kind == PaymentResultKind.Authorised && response.Action is not null)
            kind = PaymentResultKind.ShopperActionRequired;

        switch (kind)
        {
            case PaymentResultKind.Authorised when string.IsNullOrEmpty(psp):
                _logger.LogError("Adyen authorised {Reference} without a pspReference; outcome treated as unknown.", command.MerchantReference);
                return new ChargeResult(ChargeStatus.Unknown, UnknownChargeMessage(false), ResultCode: code);

            case PaymentResultKind.Authorised:
                if (response.Amount is { } authorised
                    && (authorised.Value != command.AmountMinorUnits || !string.Equals(authorised.Currency, command.Currency, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogError("Adyen authorised {AuthorisedValue} {AuthorisedCurrency} for {Reference} (pspReference {PspReference}) but {ExpectedValue} {ExpectedCurrency} was requested; reversing.",
                        authorised.Value, authorised.Currency, command.MerchantReference, psp, command.AmountMinorUnits, command.Currency);
                    return await ReverseAsync(psp!, code, command, cancellationToken);
                }
                return new ChargeResult(ChargeStatus.Authorised, "Payment authorised.", psp, code);

            case PaymentResultKind.PartiallyAuthorised:
                _logger.LogWarning("Adyen partially authorised {Reference} (pspReference {PspReference}); reversing.", command.MerchantReference, psp);
                return string.IsNullOrEmpty(psp)
                    ? new ChargeResult(ChargeStatus.ReversalUnknown, PartialMessage, psp, code)
                    : await ReverseAsync(psp, code, command, cancellationToken);

            case PaymentResultKind.Refused:
                return new ChargeResult(ChargeStatus.Declined,
                    $"Your card was declined{Detail(reason)}. No money was taken. Check the card details, contact your card issuer, or use a different card.",
                    psp, code, reason, reasonCode);

            case PaymentResultKind.Error:
                return new ChargeResult(ChargeStatus.Declined,
                    $"The payment could not be processed{Detail(reason)}. No money was taken. Please try again or use a different card.",
                    psp, code, reason, reasonCode);

            case PaymentResultKind.Cancelled:
                return new ChargeResult(ChargeStatus.Declined,
                    "The payment was cancelled before it completed. No money was taken. Please try again.",
                    psp, code, reason, reasonCode);

            case PaymentResultKind.ShopperActionRequired:
                return new ChargeResult(ChargeStatus.Declined,
                    "This card needs an extra verification step (such as 3-D Secure) that this checkout cannot perform. No money was taken. Please use a different card.",
                    psp, code, reason ?? "Shopper action required", reasonCode);

            case PaymentResultKind.NotFinal:
                _logger.LogWarning("Adyen returned non-final result {ResultCode} for {Reference} (pspReference {PspReference}).", code ?? "(none)", command.MerchantReference, psp);
                return new ChargeResult(ChargeStatus.Unknown,
                    "Adyen has not confirmed the payment yet. Repeat the payment request later to confirm it; you will never be charged twice.",
                    psp, code, TimedOut: false);

            default:
                _logger.LogError("Adyen returned unexpected result {ResultCode} for {Reference} (pspReference {PspReference}).", code ?? "(none)", command.MerchantReference, psp);
                return new ChargeResult(ChargeStatus.Unknown, UnknownChargeMessage(false), psp, code);
        }
    }

    private const string PartialMessage =
        "Your card could only cover part of the order total, so the payment was cancelled and nothing was kept. Please use a card with enough available funds.";

    private async Task<ChargeResult> ReverseAsync(string psp, string? resultCode, ChargeCommand command, CancellationToken cancellationToken)
    {
        var request = new ReversePaymentRequest
        {
            PaymentPspReference = psp,
            IdempotencyKey = "rev-" + command.IdempotencyKey,
            Body = new PaymentReversalRequest
            {
                MerchantAccount = MerchantAccount,
                Reference = command.MerchantReference,
            }
        };

        var sent = await SendSettlingAsync(token => SendReversePaymentAsync(request, token), "ReversePayment", command.MerchantReference, cancellationToken);
        if (sent.Response is not null)
        {
            _logger.LogInformation("Reversal of {PspReference} accepted: {ReversalPspReference}.", psp, sent.Response.PspReference);
            return new ChargeResult(ChargeStatus.Reversed, PartialMessage, psp, resultCode);
        }

        _logger.LogError("Reversal of {PspReference} for {Reference} did not complete ({Failure}); operator follow-up required.", psp, command.MerchantReference, sent.Failure);
        return new ChargeResult(ChargeStatus.ReversalUnknown, PartialMessage, psp, resultCode);
    }

    public async Task<RefundResult> RefundAsync(RefundCommand command, CancellationToken cancellationToken)
    {
        var request = new RefundPaymentRequest
        {
            PaymentPspReference = command.PaymentPspReference,
            IdempotencyKey = command.IdempotencyKey,
            Body = new PaymentRefundRequest
            {
                Amount = new Amount37 { Currency = command.Currency, Value = command.AmountMinorUnits },
                MerchantAccount = MerchantAccount,
                Reference = command.Reference,
            }
        };

        var sent = await SendSettlingAsync(token => SendRefundPaymentAsync(request, token), "RefundPayment", command.Reference, cancellationToken);
        if (sent.Response is { } response)
        {
            return new RefundResult(RefundGatewayStatus.Received, "Refund submitted to Adyen.", response.PspReference);
        }

        return sent.Failure switch
        {
            SendFailure.Rejected => new RefundResult(RefundGatewayStatus.Rejected, $"Adyen rejected the refund{Detail(sent.ProviderMessage)}. Nothing was refunded."),
            SendFailure.MerchantSide => new RefundResult(RefundGatewayStatus.ProviderError, "Adyen refused the request for a merchant-side reason (credentials, permissions or rate limit). Nothing was refunded."),
            _ => new RefundResult(RefundGatewayStatus.Unknown,
                sent.TimedOut
                    ? "Adyen did not respond in time, so it is not yet known whether the refund was accepted. Repeat the request to confirm it; it will never be refunded twice."
                    : "Adyen could not be reached, so it is not yet known whether the refund was accepted. Repeat the request to confirm it; it will never be refunded twice.",
                TimedOut: sent.TimedOut)
        };
    }

    // ---- one send per operation: the typed catch is per operation, the rest is shared ----

    private async Task<Sent<PaymentResponse>> SendCreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Sent<PaymentResponse>.Unknown(timedOut: true);
        try
        {
            return Sent<PaymentResponse>.Ok(await _client.Payments.CreatePayment(request, cancellationToken: cancellationToken));
        }
        catch (ApiException<CreatePaymentError> ex)
        {
            if (ex.Error.TryGetServiceError(out var serviceError))
                return FromServiceError<PaymentResponse>("CreatePayment", ex.StatusCode, serviceError);
            if (ex.Error.TryGetRawError(out RawError _))
                return FromStatus<PaymentResponse>("CreatePayment", ex.StatusCode, null, null, null);
            return FromStatus<PaymentResponse>("CreatePayment", ex.StatusCode, null, null, null);
        }
        catch (SdkException ex)
        {
            return FromSdkException<PaymentResponse>("CreatePayment", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Sent<PaymentResponse>.Unknown(timedOut: true);
        }
    }

    private async Task<Sent<PaymentRefundResponse>> SendRefundPaymentAsync(RefundPaymentRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Sent<PaymentRefundResponse>.Unknown(timedOut: true);
        try
        {
            return Sent<PaymentRefundResponse>.Ok(await _client.Modifications.RefundPayment(request, cancellationToken: cancellationToken));
        }
        catch (ApiException<RefundPaymentError> ex)
        {
            if (ex.Error.TryGetServiceError(out var serviceError))
                return FromServiceError<PaymentRefundResponse>("RefundPayment", ex.StatusCode, serviceError);
            if (ex.Error.TryGetRawError(out RawError _))
                return FromStatus<PaymentRefundResponse>("RefundPayment", ex.StatusCode, null, null, null);
            return FromStatus<PaymentRefundResponse>("RefundPayment", ex.StatusCode, null, null, null);
        }
        catch (SdkException ex)
        {
            return FromSdkException<PaymentRefundResponse>("RefundPayment", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Sent<PaymentRefundResponse>.Unknown(timedOut: true);
        }
    }

    private async Task<Sent<PaymentReversalResponse>> SendReversePaymentAsync(ReversePaymentRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Sent<PaymentReversalResponse>.Unknown(timedOut: true);
        try
        {
            return Sent<PaymentReversalResponse>.Ok(await _client.Modifications.ReversePayment(request, cancellationToken: cancellationToken));
        }
        catch (ApiException<ReversePaymentError> ex)
        {
            if (ex.Error.TryGetServiceError(out var serviceError))
                return FromServiceError<PaymentReversalResponse>("ReversePayment", ex.StatusCode, serviceError);
            if (ex.Error.TryGetRawError(out RawError _))
                return FromStatus<PaymentReversalResponse>("ReversePayment", ex.StatusCode, null, null, null);
            return FromStatus<PaymentReversalResponse>("ReversePayment", ex.StatusCode, null, null, null);
        }
        catch (SdkException ex)
        {
            return FromSdkException<PaymentReversalResponse>("ReversePayment", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Sent<PaymentReversalResponse>.Unknown(timedOut: true);
        }
    }

    /// <summary>
    /// Sends once; when the outcome is unknown (no usable answer, or an Adyen 5xx) and budget remains, resends the
    /// SAME request — same idempotency key — once, so a write that landed is returned rather than repeated.
    /// </summary>
    private async Task<Sent<T>> SendSettlingAsync<T>(Func<CancellationToken, Task<Sent<T>>> send, string operation, string reference, CancellationToken cancellationToken)
        where T : class
    {
        var first = await send(cancellationToken);
        if (first.Failure != SendFailure.Unknown || cancellationToken.IsCancellationRequested)
            return first;

        _logger.LogWarning("Adyen {Operation} for {Reference} has an unknown outcome (timed out: {TimedOut}); resending with the same idempotency key.", operation, reference, first.TimedOut);
        var second = await send(cancellationToken);
        return second.Failure == SendFailure.Unknown
            ? second with { TimedOut = first.TimedOut || second.TimedOut }
            : second;
    }

    private Sent<T> FromServiceError<T>(string operation, HttpStatusCode status, ServiceError error) where T : class
        => FromStatus<T>(operation, status, error.Message, error.ErrorCode, error.PspReference);

    private Sent<T> FromStatus<T>(string operation, HttpStatusCode status, string? message, string? errorCode, string? pspReference) where T : class
    {
        var code = (int)status;
        var failure = code switch
        {
            400 or 422 => SendFailure.Rejected,
            408 or >= 500 => SendFailure.Unknown, // Adyen may have acted; settle with the same key.
            _ => SendFailure.MerchantSide          // 401/403 credentials or permissions, 404, 429 …
        };

        if (failure == SendFailure.MerchantSide)
            _logger.LogError("Adyen {Operation} refused with HTTP {Status} (errorCode {ErrorCode}, pspReference {PspReference}); check Adyen:ApiKey / Adyen:MerchantAccount permissions.", operation, code, errorCode ?? "-", pspReference ?? "-");
        else
            _logger.LogWarning("Adyen {Operation} returned HTTP {Status} (errorCode {ErrorCode}, pspReference {PspReference}).", operation, code, errorCode ?? "-", pspReference ?? "-");

        return new Sent<T>(null, failure, status, message, errorCode);
    }

    private Sent<T> FromSdkException<T>(string operation, SdkException exception) where T : class
    {
        switch (exception)
        {
            case ResponseDeserializationException rde when (int)rde.StatusCode is >= 200 and < 300:
                // Adyen answered success but the body could not be read: the write may have happened.
                _logger.LogError(rde, "Adyen {Operation} returned an unreadable success body ({TargetType}).", operation, rde.TargetType.Name);
                return Sent<T>.Unknown(timedOut: false);
            case ApiException api:
                // An error status whose body did not match the declared error shape (or another error type).
                return FromStatus<T>(operation, api.StatusCode, null, null, null);
            case SdkTimeoutException timeout:
                _logger.LogWarning("Adyen {Operation} received no response within {Timeout}.", operation, timeout.Timeout);
                return Sent<T>.Unknown(timedOut: true);
            case SdkConnectionException connection:
                _logger.LogWarning(connection.InnerException, "Adyen {Operation} could not be completed: connection failure.", operation);
                return Sent<T>.Unknown(timedOut: false);
            case AuthSchemeException auth:
                _logger.LogError(auth, "Adyen {Operation}: the API credential could not be applied.", operation);
                return new Sent<T>(null, SendFailure.MerchantSide);
            default:
                _logger.LogError(exception, "Adyen {Operation} failed unexpectedly.", operation);
                return Sent<T>.Unknown(timedOut: false);
        }
    }

    private static string UnknownChargeMessage(bool timedOut) => timedOut
        ? "Adyen did not respond in time, so the payment outcome is not known yet. Repeat the payment request to confirm it; you will never be charged twice."
        : "Adyen could not be reached, so the payment outcome is not known yet. Repeat the payment request to confirm it; you will never be charged twice.";

    private static string Detail(string? providerText) =>
        string.IsNullOrWhiteSpace(providerText) ? string.Empty : $" ({providerText.Trim().TrimEnd('.')})";

    private enum PaymentResultKind { Authorised, PartiallyAuthorised, Refused, Error, Cancelled, ShopperActionRequired, NotFinal, Unexpected }

    private enum SendFailure { None, Rejected, MerchantSide, Unknown }

    private sealed record Sent<T>(T? Response, SendFailure Failure, HttpStatusCode? Status = null, string? ProviderMessage = null, string? ProviderErrorCode = null, bool TimedOut = false)
        where T : class
    {
        public static Sent<T> Ok(T response) => new(response, SendFailure.None);
        public static Sent<T> Unknown(bool timedOut) => new(null, SendFailure.Unknown, TimedOut: timedOut);
    }
}
