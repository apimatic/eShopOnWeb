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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Card payments and refunds through Adyen Checkout. This is the one place SDK failures are translated:
/// every exit is either a result or a <see cref="PaymentGatewayException"/> carrying a caller-safe message.
/// </summary>
public class AdyenPaymentGateway : IPaymentGateway
{
    private readonly AdyenApIsClient _client;
    private readonly AdyenSettings _settings;
    private readonly IAppLogger<AdyenPaymentGateway> _logger;

    public AdyenPaymentGateway(AdyenApIsClient client, IOptions<AdyenSettings> settings, IAppLogger<AdyenPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.Currency.Trim().ToUpperInvariant();

    public async Task<CardPaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken deadline)
    {
        var call = new CreatePaymentRequest
        {
            // A real, caller-supplied key: re-sending this record can never create a second payment.
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
                Reference = request.MerchantReference,
                MerchantOrderReference = $"eshop-order-{request.OrderId}",
                ReturnUrl = _settings.ReturnUrl,
                ShopperInteraction = ShopperInteraction.Ecommerce,
                // Take the money now: capture immediately after authorisation.
                CaptureDelayHours = 0,
            },
        };

        var response = await SendAsync<PaymentResponse, CreatePaymentError>(
            "CreatePayment",
            request.MerchantReference,
            token => _client.Payments.CreatePayment(call, cancellationToken: token),
            error => error.TryGetServiceError(out var serviceError) ? serviceError : null,
            deadline);

        if (response.ResultCode is null)
        {
            _logger.LogError(null, "Adyen CreatePayment for {Reference} returned no resultCode (psp {PspReference}).",
                request.MerchantReference, response.PspReference ?? "none");
            throw new PaymentGatewayException(PaymentGatewayFailure.OutcomeUnknown,
                "Adyen answered without a payment result. The outcome is being confirmed; paying again is safe and will not charge twice.")
            {
                ProviderStatusCode = (int)HttpStatusCode.OK,
                ProviderReference = response.PspReference,
            };
        }

        var outcome = response.ResultCode.Match(
            onAuthenticationFinished: () => CardPaymentOutcome.Unsupported,
            onAuthenticationNotRequired: () => CardPaymentOutcome.Unsupported,
            onAuthorised: () => CardPaymentOutcome.Authorised,
            onCancelled: () => CardPaymentOutcome.Cancelled,
            onChallengeShopper: () => CardPaymentOutcome.ActionRequired,
            onError: () => CardPaymentOutcome.Error,
            onIdentifyShopper: () => CardPaymentOutcome.ActionRequired,
            onPartiallyAuthorised: () => CardPaymentOutcome.Unsupported,
            onPending: () => CardPaymentOutcome.Pending,
            onPresentToShopper: () => CardPaymentOutcome.ActionRequired,
            onReceived: () => CardPaymentOutcome.Pending,
            onRedirectShopper: () => CardPaymentOutcome.ActionRequired,
            onRefused: () => CardPaymentOutcome.Refused,
            onSuccess: () => CardPaymentOutcome.Unsupported,
            otherwise: _ => CardPaymentOutcome.Unsupported);

        return new CardPaymentResult(
            outcome,
            response.ResultCode.Value,
            response.PspReference,
            response.RefusalReason,
            response.RefusalReasonCode,
            response.Amount?.Currency,
            response.Amount?.Value);
    }

    public async Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken deadline)
    {
        var call = new RefundPaymentRequest
        {
            PaymentPspReference = request.PaymentPspReference,
            IdempotencyKey = request.IdempotencyKey,
            Body = new PaymentRefundRequest
            {
                Amount = new Amount37 { Currency = request.Currency, Value = request.AmountMinor },
                MerchantAccount = _settings.MerchantAccount,
                Reference = request.MerchantReference,
                MerchantRefundReason = request.Reason is not null && MerchantRefundReason.TryGetKnownValue(request.Reason, out var reason)
                    ? reason
                    : null,
            },
        };

        var response = await SendAsync<PaymentRefundResponse, RefundPaymentError>(
            "RefundPayment",
            request.MerchantReference,
            token => _client.Modifications.RefundPayment(call, cancellationToken: token),
            error => error.TryGetServiceError(out var serviceError) ? serviceError : null,
            deadline);

        return new ProviderRefundResult(response.PspReference, response.Status);
    }

    /// <summary>
    /// Runs one Adyen write under the caller's deadline. A transport failure may have reached Adyen, so the same
    /// request record (same idempotency key) is re-sent once while the deadline allows; whatever is still
    /// unresolved after that is reported as an unknown outcome for the caller to record and settle.
    /// </summary>
    private async Task<TResponse> SendAsync<TResponse, TError>(
        string operation,
        string reference,
        Func<CancellationToken, Task<TResponse>> call,
        Func<TError, ServiceError?> readServiceError,
        CancellationToken deadline)
        where TError : ApiError
    {
        var resent = false;
        while (true)
        {
            try
            {
                return await call(deadline);
            }
            catch (ApiException<TError> ex)
            {
                var serviceError = readServiceError(ex.Error);
                if (serviceError is null && ex.Error.TryGetRawError(out RawError raw))
                {
                    _logger.LogWarning("Adyen {Operation} for {Reference}: HTTP {Status} with an unrecognised error body.",
                        operation, reference, (int)raw.StatusCode);
                }
                throw FromProviderError(operation, reference, ex.StatusCode, serviceError, ex);
            }
            catch (ResponseDeserializationException ex)
            {
                _logger.LogError(ex, "Adyen {Operation} for {Reference}: HTTP {Status} body could not be read as {TargetType}.",
                    operation, reference, (int)ex.StatusCode, ex.TargetType.Name);
                throw FromProviderError(operation, reference, ex.StatusCode, null, ex);
            }
            catch (SdkConnectionException ex) when (!resent && !deadline.IsCancellationRequested)
            {
                // Includes SdkTimeoutException. The request may have reached Adyen: re-send the identical request.
                resent = true;
                _logger.LogWarning("Adyen {Operation} for {Reference}: {Failure}; re-sending once with the same idempotency key.",
                    operation, reference, ex.GetType().Name);
            }
            catch (SdkTimeoutException ex)
            {
                _logger.LogError(ex, "Adyen {Operation} for {Reference}: no response within {Timeout}.", operation, reference, ex.Timeout);
                throw NoResponse(ex);
            }
            catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
            {
                _logger.LogError(ex, "Adyen {Operation} for {Reference}: no response within the request's time budget.", operation, reference);
                throw NoResponse(ex);
            }
            catch (SdkConnectionException ex)
            {
                _logger.LogError(ex, "Adyen {Operation} for {Reference}: could not reach Adyen.", operation, reference);
                throw new PaymentGatewayException(PaymentGatewayFailure.OutcomeUnknown,
                    "Adyen could not be reached. The outcome is being confirmed; retrying is safe and will not charge or refund twice.", ex);
            }
            catch (AuthSchemeException ex)
            {
                _logger.LogError(ex, "Adyen {Operation} for {Reference}: the API key could not be applied.", operation, reference);
                throw new PaymentGatewayException(PaymentGatewayFailure.Unavailable,
                    "The payment provider is not available right now. Please try again later.", ex);
            }
        }
    }

    private static PaymentGatewayException NoResponse(Exception cause) =>
        new(PaymentGatewayFailure.OutcomeUnknown,
            "Adyen did not respond in time. The outcome is being confirmed; retrying is safe and will not charge or refund twice.", cause)
        {
            TimedOut = true,
        };

    private PaymentGatewayException FromProviderError(string operation, string reference, HttpStatusCode statusCode,
        ServiceError? serviceError, Exception cause)
    {
        var status = (int)statusCode;
        _logger.LogError(null,
            "Adyen {Operation} for {Reference} failed: HTTP {Status}, errorCode {ErrorCode}, errorType {ErrorType}, message {ProviderMessage}, pspReference {PspReference}.",
            operation, reference, status, serviceError?.ErrorCode ?? "none", serviceError?.ErrorType ?? "none",
            serviceError?.Message ?? "none", serviceError?.PspReference ?? "none");

        PaymentGatewayException Build(PaymentGatewayFailure failure, string message) => new(failure, message, cause)
        {
            ProviderStatusCode = status,
            ProviderErrorCode = serviceError?.ErrorCode,
            ProviderReference = serviceError?.PspReference,
        };

        if (status is >= 200 and < 300)
        {
            // Adyen answered success but we cannot read what it did.
            return Build(PaymentGatewayFailure.OutcomeUnknown,
                "Adyen's answer could not be read. The outcome is being confirmed; retrying is safe and will not charge or refund twice.");
        }
        if (status is 401 or 403)
        {
            // Our credentials, not the caller's fault.
            return Build(PaymentGatewayFailure.Unavailable, "The payment provider is not available right now. Please try again later.");
        }
        if (status == 429)
        {
            return Build(PaymentGatewayFailure.Unavailable, "The payment provider is busy right now. Please try again in a moment.");
        }
        if (status >= 500)
        {
            return Build(PaymentGatewayFailure.OutcomeUnknown,
                "The payment provider reported an internal error. The outcome is being confirmed; retrying is safe and will not charge or refund twice.");
        }

        var detail = string.IsNullOrWhiteSpace(serviceError?.Message) ? $"HTTP {status}" : serviceError!.Message;
        return Build(PaymentGatewayFailure.Rejected, $"Adyen rejected the request: {detail}.");
    }
}
