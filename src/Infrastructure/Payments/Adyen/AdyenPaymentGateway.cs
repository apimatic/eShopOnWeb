using System;
using System.Net;
using System.Text.Json;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

/// <summary>
/// <see cref="IPaymentGateway"/> over the Adyen Checkout API (Payments.CreatePayment, Modifications.RefundPayment).
/// Every provider failure is translated into an outcome here; nothing from the SDK escapes this class.
/// </summary>
public class AdyenPaymentGateway : IPaymentGateway
{
    /// <summary>The bound on one Adyen call, end to end. A write that hits it has an unknown outcome.</summary>
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);

    private readonly AdyenApIsClient _client;
    private readonly AdyenSettings _settings;
    private readonly ILogger<AdyenPaymentGateway> _logger;

    public AdyenPaymentGateway(AdyenApIsClient client, IOptions<AdyenSettings> settings, ILogger<AdyenPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.NormalizedCurrency;

    public async Task<PaymentProviderResult> AuthoriseCardPaymentAsync(CardPaymentRequest request, CancellationToken cancellationToken)
    {
        var sdkRequest = new CreatePaymentRequest
        {
            // The attempt's own key: a replay of this exact message is answered with the original result.
            IdempotencyKey = request.IdempotencyKey,
            Body = new PaymentRequest
            {
                Amount = new Amount2 { Currency = request.Currency, Value = request.AmountMinorUnits },
                MerchantAccount = _settings.MerchantAccount,
                Reference = request.MerchantReference,
                ReturnUrl = _settings.ReturnUrl!,
                PaymentMethod = PaymentMethod111.Card(new Card
                {
                    Type = Type12.Scheme,
                    EncryptedCardNumber = request.Card.EncryptedCardNumber,
                    EncryptedExpiryMonth = request.Card.EncryptedExpiryMonth,
                    EncryptedExpiryYear = request.Card.EncryptedExpiryYear,
                    EncryptedSecurityCode = request.Card.EncryptedSecurityCode,
                    HolderName = request.Card.HolderName,
                }),
                ShopperInteraction = ShopperInteraction.Ecommerce,
                Channel = Channel2.Web,
                // Capture immediately on authorisation: the money is taken now, whatever the account default.
                CaptureDelayHours = 0,
            }
        };

        var result = await SendPaymentAsync(sdkRequest, request.MerchantReference, cancellationToken);
        if (result.Outcome != PaymentProviderOutcome.Unknown)
        {
            return result;
        }

        // The payment may have been taken. Settle it now by replaying the identical message under the same
        // idempotency key: Adyen answers a replay with the original outcome instead of charging again.
        _logger.LogWarning("Adyen payment {Reference}: outcome unknown, replaying with the same idempotency key",
            request.MerchantReference);
        var replay = await SendPaymentAsync(sdkRequest, request.MerchantReference, cancellationToken);
        return replay.Outcome == PaymentProviderOutcome.Unknown && replay.RawResponse is null
            ? replay with { RawResponse = result.RawResponse, HttpStatus = replay.HttpStatus ?? result.HttpStatus }
            : replay;
    }

    public async Task<RefundProviderResult> RefundAsync(ProviderRefundRequest request, CancellationToken cancellationToken)
    {
        var sdkRequest = new RefundPaymentRequest
        {
            PaymentPspReference = request.PaymentPspReference,
            IdempotencyKey = request.IdempotencyKey,
            Body = new PaymentRefundRequest
            {
                Amount = new Amount37 { Currency = request.Currency, Value = request.AmountMinorUnits },
                MerchantAccount = _settings.MerchantAccount,
                Reference = request.MerchantReference,
            }
        };

        var result = await SendRefundAsync(sdkRequest, request.MerchantReference, cancellationToken);
        if (result.Outcome != RefundProviderOutcome.Unknown)
        {
            return result;
        }

        _logger.LogWarning("Adyen refund {Reference}: outcome unknown, replaying with the same idempotency key",
            request.MerchantReference);
        var replay = await SendRefundAsync(sdkRequest, request.MerchantReference, cancellationToken);
        return replay.Outcome == RefundProviderOutcome.Unknown && replay.RawResponse is null
            ? replay with { RawResponse = result.RawResponse, HttpStatus = replay.HttpStatus ?? result.HttpStatus }
            : replay;
    }

    private async Task<PaymentProviderResult> SendPaymentAsync(CreatePaymentRequest sdkRequest, string reference,
        CancellationToken cancellationToken)
    {
        var capture = new AdyenRawResponseCapture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);
        try
        {
            var response = await _client.Payments.CreatePayment(sdkRequest, capture.RequestOptions, deadline.Token);
            return FromPaymentResponse(response.ResultCode?.Value, response.PspReference, response.RefusalReason,
                response.RefusalReasonCode, capture);
        }
        catch (ApiException<CreatePaymentError> ex)
        {
            if (ex.Error.TryGetServiceError(out var serviceError))
            {
                return PaymentFailure(ex.StatusCode, serviceError, capture, reference);
            }

            var rawBody = ex.Error.TryGetRawError(out RawError raw) ? raw.ReadAsString() : null;
            return PaymentFailure(ex.StatusCode, null, capture, reference, rawBody);
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode))
        {
            // Adyen answered 2xx but the body no longer matches the generated model. Read the fields
            // that decide the outcome straight from the captured body; without them the outcome is unknown.
            _logger.LogWarning(ex, "Adyen payment {Reference}: 2xx body did not match the SDK model", reference);
            return FromRawPaymentBody(capture);
        }
        catch (ResponseDeserializationException ex)
        {
            return PaymentFailure(ex.StatusCode, null, capture, reference);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Adyen payment {Reference}: credentials could not be applied", reference);
            return new PaymentProviderResult { Outcome = PaymentProviderOutcome.ProviderUnavailable, ErrorMessage = "Payment provider credentials are not usable." };
        }
        catch (SdkException ex)
        {
            // Connection failure or timeout (SdkConnectionException / SdkTimeoutException): the request may have
            // reached Adyen, so this is an unknown outcome, never a failure.
            _logger.LogWarning(ex, "Adyen payment {Reference}: no usable response", reference);
            return UnknownPayment(capture);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Adyen payment {Reference}: no response within {Budget}", reference, CallBudget);
            return UnknownPayment(capture);
        }
    }

    private async Task<RefundProviderResult> SendRefundAsync(RefundPaymentRequest sdkRequest, string reference,
        CancellationToken cancellationToken)
    {
        var capture = new AdyenRawResponseCapture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);
        try
        {
            var response = await _client.Modifications.RefundPayment(sdkRequest, capture.RequestOptions, deadline.Token);
            return new RefundProviderResult
            {
                Outcome = RefundProviderOutcome.Received,
                PspReference = response.PspReference,
                ProviderStatus = response.Status,
                HttpStatus = capture.StatusCode,
                RawResponse = capture.Body,
            };
        }
        catch (ApiException<RefundPaymentError> ex)
        {
            if (ex.Error.TryGetServiceError(out var serviceError))
            {
                return RefundFailure(ex.StatusCode, serviceError, capture, reference);
            }

            var rawBody = ex.Error.TryGetRawError(out RawError raw) ? raw.ReadAsString() : null;
            return RefundFailure(ex.StatusCode, null, capture, reference, rawBody);
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode))
        {
            _logger.LogWarning(ex, "Adyen refund {Reference}: 2xx body did not match the SDK model", reference);
            var pspReference = ReadString(capture.Body, "pspReference");
            return pspReference is null
                ? UnknownRefund(capture)
                : new RefundProviderResult
                {
                    Outcome = RefundProviderOutcome.Received,
                    PspReference = pspReference,
                    ProviderStatus = ReadString(capture.Body, "status"),
                    HttpStatus = capture.StatusCode,
                    RawResponse = capture.Body,
                };
        }
        catch (ResponseDeserializationException ex)
        {
            return RefundFailure(ex.StatusCode, null, capture, reference);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Adyen refund {Reference}: credentials could not be applied", reference);
            return new RefundProviderResult { Outcome = RefundProviderOutcome.ProviderUnavailable, ErrorMessage = "Payment provider credentials are not usable." };
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Adyen refund {Reference}: no usable response", reference);
            return UnknownRefund(capture);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Adyen refund {Reference}: no response within {Budget}", reference, CallBudget);
            return UnknownRefund(capture);
        }
    }

    private static PaymentProviderResult FromPaymentResponse(string? resultCode, string? pspReference, string? refusalReason,
        string? refusalReasonCode, AdyenRawResponseCapture capture)
    {
        var outcome = ResultCode1.TryGetKnownValue(resultCode, out var known)
            ? known.Match(
                onAuthenticationFinished: () => PaymentProviderOutcome.Pending,
                onAuthenticationNotRequired: () => PaymentProviderOutcome.Pending,
                onAuthorised: () => PaymentProviderOutcome.Authorised,
                onCancelled: () => PaymentProviderOutcome.Refused,
                onChallengeShopper: () => PaymentProviderOutcome.ActionRequired,
                onError: () => PaymentProviderOutcome.Refused,
                onIdentifyShopper: () => PaymentProviderOutcome.ActionRequired,
                // Authorised for less than the order total: not paid, and held for an operator to resolve.
                onPartiallyAuthorised: () => PaymentProviderOutcome.Pending,
                onPending: () => PaymentProviderOutcome.Pending,
                onPresentToShopper: () => PaymentProviderOutcome.ActionRequired,
                onReceived: () => PaymentProviderOutcome.Pending,
                onRedirectShopper: () => PaymentProviderOutcome.ActionRequired,
                onRefused: () => PaymentProviderOutcome.Refused,
                onSuccess: () => PaymentProviderOutcome.Pending,
                otherwise: _ => PaymentProviderOutcome.Pending)
            // No result code at all: Adyen answered, but we cannot tell what happened.
            : resultCode is null ? PaymentProviderOutcome.Unknown : PaymentProviderOutcome.Pending;

        return new PaymentProviderResult
        {
            Outcome = outcome,
            PspReference = pspReference,
            ResultCode = resultCode,
            RefusalReason = refusalReason,
            RefusalReasonCode = refusalReasonCode,
            HttpStatus = capture.StatusCode,
            RawResponse = capture.Body,
        };
    }

    private static PaymentProviderResult FromRawPaymentBody(AdyenRawResponseCapture capture) =>
        FromPaymentResponse(
            ReadString(capture.Body, "resultCode"),
            ReadString(capture.Body, "pspReference"),
            ReadString(capture.Body, "refusalReason"),
            ReadString(capture.Body, "refusalReasonCode"),
            capture);

    private PaymentProviderResult PaymentFailure(HttpStatusCode status, ServiceError? error, AdyenRawResponseCapture capture,
        string reference, string? rawBody = null)
    {
        var outcome = Classify(status) switch
        {
            FailureKind.Rejected => PaymentProviderOutcome.Rejected,
            FailureKind.OurFault => PaymentProviderOutcome.ProviderUnavailable,
            _ => PaymentProviderOutcome.Unknown
        };
        LogFailure("payment", reference, status, error);
        return new PaymentProviderResult
        {
            Outcome = outcome,
            PspReference = error?.PspReference,
            ErrorCode = error?.ErrorCode,
            ErrorMessage = error?.Message,
            HttpStatus = (int)status,
            RawResponse = capture.Body ?? rawBody,
        };
    }

    private RefundProviderResult RefundFailure(HttpStatusCode status, ServiceError? error, AdyenRawResponseCapture capture,
        string reference, string? rawBody = null)
    {
        var outcome = Classify(status) switch
        {
            FailureKind.Rejected => RefundProviderOutcome.Rejected,
            FailureKind.OurFault => RefundProviderOutcome.ProviderUnavailable,
            _ => RefundProviderOutcome.Unknown
        };
        LogFailure("refund", reference, status, error);
        return new RefundProviderResult
        {
            Outcome = outcome,
            PspReference = error?.PspReference,
            ErrorCode = error?.ErrorCode,
            ErrorMessage = error?.Message,
            HttpStatus = (int)status,
            RawResponse = capture.Body ?? rawBody,
        };
    }

    private void LogFailure(string operation, string reference, HttpStatusCode status, ServiceError? error) =>
        _logger.LogWarning(
            "Adyen {Operation} {Reference} returned {Status}: errorCode {ErrorCode}, errorType {ErrorType}, pspReference {PspReference}",
            operation, reference, (int)status, error?.ErrorCode ?? "-", error?.ErrorType ?? "-", error?.PspReference ?? "-");

    private enum FailureKind { Rejected, OurFault, Unknown }

    private static FailureKind Classify(HttpStatusCode status) => (int)status switch
    {
        // Our credentials, permissions or quota: nothing was processed, and the caller cannot fix it.
        401 or 403 or 429 => FailureKind.OurFault,
        // Adyen may still have processed a request it timed out on or failed internally.
        408 or >= 500 => FailureKind.Unknown,
        // Validation and every other client error: the request was refused as sent.
        >= 400 => FailureKind.Rejected,
        _ => FailureKind.Unknown
    };

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static PaymentProviderResult UnknownPayment(AdyenRawResponseCapture capture) => new()
    {
        Outcome = PaymentProviderOutcome.Unknown,
        HttpStatus = capture.StatusCode,
        RawResponse = capture.Body,
    };

    private static RefundProviderResult UnknownRefund(AdyenRawResponseCapture capture) => new()
    {
        Outcome = RefundProviderOutcome.Unknown,
        HttpStatus = capture.StatusCode,
        RawResponse = capture.Body,
    };

    private static string? ReadString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(property, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
