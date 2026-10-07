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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Payments.Adyen;

/// <summary>
/// The one place the Adyen SDK is called. Every SDK failure is converted here into a <see cref="ProviderCallOutcome"/>;
/// nothing Adyen-specific (or an SDK exception message) leaks past this class.
/// </summary>
public sealed class AdyenPaymentGateway : IPaymentGateway
{
    private readonly AdyenApIsClient _client;
    private readonly AdyenSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<AdyenPaymentGateway> _logger;

    public AdyenPaymentGateway(AdyenApIsClient client, IOptions<AdyenSettings> settings, TimeProvider time,
        ILogger<AdyenPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    public string Currency => _settings.Currency.Trim();

    public async Task<PaymentAuthorisationResult> AuthoriseAsync(PaymentAuthorisationRequest request, CancellationToken cancellationToken)
    {
        var capture = new AdyenResponseCapture(_time);
        var sdkRequest = new CreatePaymentRequest
        {
            IdempotencyKey = request.IdempotencyKey,
            Body = new PaymentRequest
            {
                Amount = new Amount2 { Currency = request.Currency, Value = request.AmountInMinorUnits },
                MerchantAccount = _settings.MerchantAccount,
                Reference = request.Reference,
                ReturnUrl = _settings.ReturnUrl,
                PaymentMethod = PaymentMethod111.Card(new Card
                {
                    Type = Type12.Scheme,
                    EncryptedCardNumber = request.Card.EncryptedCardNumber,
                    EncryptedExpiryMonth = request.Card.EncryptedExpiryMonth,
                    EncryptedExpiryYear = request.Card.EncryptedExpiryYear,
                    EncryptedSecurityCode = request.Card.EncryptedSecurityCode,
                    HolderName = request.Card.HolderName,
                }),
                // Take the money now: auto-capture with no delay after authorisation.
                CaptureDelayHours = 0,
                ShopperInteraction = ShopperInteraction.Ecommerce,
                MerchantOrderReference = $"eshop-order-{request.OrderId}",
            }
        };

        try
        {
            var response = await _client.Payments.CreatePayment(sdkRequest, capture.RequestOptions, cancellationToken);
            return ToPaymentResult(response, capture);
        }
        catch (ApiException<CreatePaymentError> ex)
        {
            if (ex.Error.TryGetServiceError(out var serviceError))
                return PaymentFailure(ex.StatusCode, serviceError, capture, request.Reference);
            if (ex.Error.TryGetRawError(out RawError raw))
                return PaymentFailure(raw.StatusCode, null, capture, request.Reference);
            return PaymentFailure(ex.StatusCode, null, capture, request.Reference);
        }
        catch (ResponseDeserializationException ex)
        {
            // 2xx: Adyen answered but the body is unreadable — the payment may have gone through.
            // Non-2xx: Adyen rejected the request; only the error detail was lost.
            _logger.LogError("Adyen payment response for {Reference} (HTTP {Status}) could not be read as {Type}.",
                request.Reference, (int)ex.StatusCode, ex.TargetType.Name);
            return PaymentFailure(ex.StatusCode, null, capture, request.Reference, unreadableSuccess: IsSuccess(ex.StatusCode));
        }
        catch (Exception ex) when (IsNoResponse(ex, cancellationToken, out var note))
        {
            _logger.LogWarning("No response from Adyen for payment {Reference}: {Note}", request.Reference, note);
            capture.AddNote(note);
            return new PaymentAuthorisationResult
            {
                Outcome = ProviderCallOutcome.Unknown,
                NoResponse = true,
                ErrorMessage = "Adyen did not respond.",
                Responses = capture.Responses
            };
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex.InnerException, "The Adyen credential could not be applied for payment {Reference}.", request.Reference);
            capture.AddNote("The Adyen credential could not be applied; nothing was sent.");
            return new PaymentAuthorisationResult
            {
                Outcome = ProviderCallOutcome.ProviderUnavailable,
                ErrorMessage = "The payment provider is not configured correctly.",
                Responses = capture.Responses
            };
        }
    }

    public async Task<PaymentRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken cancellationToken)
    {
        var capture = new AdyenResponseCapture(_time);
        var sdkRequest = new RefundPaymentRequest
        {
            PaymentPspReference = request.PaymentPspReference,
            IdempotencyKey = request.IdempotencyKey,
            Body = new PaymentRefundRequest
            {
                Amount = new Amount37 { Currency = request.Currency, Value = request.AmountInMinorUnits },
                MerchantAccount = _settings.MerchantAccount,
                Reference = request.Reference,
                MerchantRefundReason = ToAdyenReason(request.Reason),
            }
        };

        try
        {
            var response = await _client.Modifications.RefundPayment(sdkRequest, capture.RequestOptions, cancellationToken);
            return new PaymentRefundResult
            {
                Outcome = ProviderCallOutcome.Accepted,
                PspReference = response.PspReference,
                HttpStatus = LastStatus(capture),
                Responses = capture.Responses
            };
        }
        catch (ApiException<RefundPaymentError> ex)
        {
            if (ex.Error.TryGetServiceError(out var serviceError))
                return RefundFailure(ex.StatusCode, serviceError, capture, request.Reference);
            if (ex.Error.TryGetRawError(out RawError raw))
                return RefundFailure(raw.StatusCode, null, capture, request.Reference);
            return RefundFailure(ex.StatusCode, null, capture, request.Reference);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError("Adyen refund response for {Reference} (HTTP {Status}) could not be read as {Type}.",
                request.Reference, (int)ex.StatusCode, ex.TargetType.Name);
            return RefundFailure(ex.StatusCode, null, capture, request.Reference, unreadableSuccess: IsSuccess(ex.StatusCode));
        }
        catch (Exception ex) when (IsNoResponse(ex, cancellationToken, out var note))
        {
            _logger.LogWarning("No response from Adyen for refund {Reference}: {Note}", request.Reference, note);
            capture.AddNote(note);
            return new PaymentRefundResult
            {
                Outcome = ProviderCallOutcome.Unknown,
                NoResponse = true,
                ErrorMessage = "Adyen did not respond.",
                Responses = capture.Responses
            };
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex.InnerException, "The Adyen credential could not be applied for refund {Reference}.", request.Reference);
            capture.AddNote("The Adyen credential could not be applied; nothing was sent.");
            return new PaymentRefundResult
            {
                Outcome = ProviderCallOutcome.ProviderUnavailable,
                ErrorMessage = "The payment provider is not configured correctly.",
                Responses = capture.Responses
            };
        }
    }

    private PaymentAuthorisationResult ToPaymentResult(PaymentResponse response, AdyenResponseCapture capture)
    {
        var resultCode = response.ResultCode;
        ProviderCallOutcome outcome;
        if (resultCode is null)
            outcome = ProviderCallOutcome.Unknown;
        else if (resultCode == ResultCode1.Authorised)
            outcome = ProviderCallOutcome.Authorised;
        else if (resultCode == ResultCode1.Refused || resultCode == ResultCode1.Error || resultCode == ResultCode1.Cancelled)
            outcome = ProviderCallOutcome.Refused;
        else if (resultCode == ResultCode1.Pending || resultCode == ResultCode1.Received)
            outcome = ProviderCallOutcome.Pending;
        else if (resultCode == ResultCode1.PartiallyAuthorised)
            outcome = ProviderCallOutcome.PartiallyAuthorised;
        else if (resultCode == ResultCode1.RedirectShopper || resultCode == ResultCode1.IdentifyShopper
                 || resultCode == ResultCode1.ChallengeShopper || resultCode == ResultCode1.PresentToShopper
                 || response.Action is not null)
            outcome = ProviderCallOutcome.ActionRequired;
        else
            outcome = ProviderCallOutcome.Unknown;

        if (outcome == ProviderCallOutcome.Unknown)
            _logger.LogWarning("Adyen returned result code {ResultCode} for {Reference}; treated as unknown.",
                resultCode?.Value ?? "(none)", response.MerchantReference ?? "-");

        return new PaymentAuthorisationResult
        {
            Outcome = outcome,
            PspReference = response.PspReference,
            ResultCode = resultCode?.Value,
            RefusalReason = response.RefusalReason,
            RefusalReasonCode = response.RefusalReasonCode,
            HttpStatus = LastStatus(capture),
            AuthorisedAmountInMinorUnits = response.Amount?.Value,
            AuthorisedCurrency = response.Amount?.Currency,
            Responses = capture.Responses
        };
    }

    private PaymentAuthorisationResult PaymentFailure(HttpStatusCode status, ServiceError? error, AdyenResponseCapture capture,
        string reference, bool unreadableSuccess = false)
    {
        var outcome = unreadableSuccess ? ProviderCallOutcome.Unknown : Classify(status);
        LogFailure("payment", reference, status, error, outcome);
        return new PaymentAuthorisationResult
        {
            Outcome = outcome,
            PspReference = error?.PspReference,
            ErrorCode = error?.ErrorCode,
            ErrorMessage = error?.Message ?? $"Adyen returned HTTP {(int)status}.",
            HttpStatus = (int)status,
            Responses = capture.Responses
        };
    }

    private PaymentRefundResult RefundFailure(HttpStatusCode status, ServiceError? error, AdyenResponseCapture capture,
        string reference, bool unreadableSuccess = false)
    {
        var outcome = unreadableSuccess ? ProviderCallOutcome.Unknown : Classify(status);
        LogFailure("refund", reference, status, error, outcome);
        return new PaymentRefundResult
        {
            Outcome = outcome,
            PspReference = error?.PspReference,
            ErrorCode = error?.ErrorCode,
            ErrorMessage = error?.Message ?? $"Adyen returned HTTP {(int)status}.",
            HttpStatus = (int)status,
            Responses = capture.Responses
        };
    }

    /// <summary>
    /// 401/403 are our credentials and 429 our quota — the caller did nothing wrong; other 4xx reject the caller's
    /// request; 5xx (and anything unmapped) leave the outcome unknown, so it is settled rather than reported as failed.
    /// </summary>
    internal static ProviderCallOutcome Classify(HttpStatusCode status) => (int)status switch
    {
        401 or 403 or 429 => ProviderCallOutcome.ProviderUnavailable,
        >= 400 and < 500 => ProviderCallOutcome.Rejected,
        _ => ProviderCallOutcome.Unknown
    };

    private void LogFailure(string operation, string reference, HttpStatusCode status, ServiceError? error, ProviderCallOutcome outcome)
    {
        var level = outcome == ProviderCallOutcome.Rejected ? LogLevel.Warning : LogLevel.Error;
        _logger.Log(level, "Adyen {Operation} {Reference} failed: HTTP {Status}, errorCode {ErrorCode}, errorType {ErrorType}, pspReference {PspReference}, outcome {Outcome}.",
            operation, reference, (int)status, error?.ErrorCode ?? "-", error?.ErrorType ?? "-", error?.PspReference ?? "-", outcome);
    }

    /// <summary>
    /// No usable response: the SDK's per-attempt timeout, a connection failure, or this request's own time budget
    /// running out (the caller's token — the SDK never wraps that one).
    /// </summary>
    private static bool IsNoResponse(Exception ex, CancellationToken cancellationToken, out string note)
    {
        switch (ex)
        {
            case SdkTimeoutException timeout:
                note = $"No response from Adyen within {timeout.Timeout.TotalSeconds:0.#} s.";
                return true;
            case SdkConnectionException:
                note = "The connection to Adyen failed before a complete response arrived.";
                return true;
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                note = "No response from Adyen within this request's time budget.";
                return true;
            default:
                note = "";
                return false;
        }
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static int? LastStatus(AdyenResponseCapture capture)
    {
        var responses = capture.Responses;
        return responses.Count == 0 ? null : responses[^1].HttpStatus;
    }

    private static MerchantRefundReason? ToAdyenReason(RefundReason? reason) => reason switch
    {
        RefundReason.Fraud => MerchantRefundReason.Fraud,
        RefundReason.CustomerRequest => MerchantRefundReason.CustomerRequest,
        RefundReason.Return => MerchantRefundReason.Return,
        RefundReason.Duplicate => MerchantRefundReason.Duplicate,
        RefundReason.Other => MerchantRefundReason.Other,
        _ => null
    };
}
