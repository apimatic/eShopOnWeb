using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdyenApIs;
using AdyenApIs.Core.Exceptions;
using AdyenApIs.Errors;
using AdyenApIs.Models;
using AdyenApIs.Models.AnyOf;
using AdyenApIs.Models.Enums;
using AdyenApIs.Requests.Modifications;
using AdyenApIs.Requests.Payments;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

/// <summary>
/// Takes and gives back card payments through Adyen Checkout. Every SDK failure is translated here into a result
/// value; nothing from the SDK escapes. Request bodies (encrypted card data, holder name) are never logged.
/// </summary>
public sealed class AdyenPaymentGateway : IPaymentGateway
{
    public const string Provider = "Adyen";

    // Extra time on top of the SDK's per-call timeout before our own deadline cancels the call.
    private static readonly TimeSpan DeadlineGrace = TimeSpan.FromSeconds(2);

    private readonly AdyenApIsClient _client;
    private readonly AdyenSettings _settings;
    private readonly ILogger<AdyenPaymentGateway> _logger;

    public AdyenPaymentGateway(AdyenApIsClient client, IOptions<AdyenSettings> settings, ILogger<AdyenPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string ProviderName => Provider;

    public string Currency => _settings.Currency!.Trim().ToUpperInvariant();

    public async Task<CardPaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var capture = new AdyenResponseCapture();
        var body = new PaymentRequest
        {
            Amount = new Amount2 { Currency = request.Currency, Value = request.AmountMinor },
            MerchantAccount = _settings.MerchantAccount!,
            PaymentMethod = PaymentMethod111.Card(new Card
            {
                EncryptedCardNumber = request.Card.EncryptedCardNumber,
                EncryptedExpiryMonth = request.Card.EncryptedExpiryMonth,
                EncryptedExpiryYear = request.Card.EncryptedExpiryYear,
                EncryptedSecurityCode = request.Card.EncryptedSecurityCode,
                HolderName = request.Card.HolderName,
            }),
            Reference = request.MerchantReference,
            ReturnUrl = _settings.ReturnUrl!,
            // Capture immediately after authorisation: the money is taken now.
            CaptureDelayHours = 0,
        };

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_settings.Timeout + DeadlineGrace);
        try
        {
            var response = await _client.Payments.CreatePayment(
                new CreatePaymentRequest { IdempotencyKey = request.IdempotencyKey, Body = body },
                capture.RequestOptions,
                deadline.Token);

            return MapPayment(request, response.ResultCode?.Value, response.PspReference, response.RefusalReason,
                response.RefusalReasonCode, response.Amount?.Value, response.Amount?.Currency, capture);
        }
        catch (ApiException<CreatePaymentError> ex)
        {
            // Statuses without a typed body fall to TryGetRawError; their body is already in the capture verbatim.
            var error = ex.Error.TryGetServiceError(out var serviceError) ? serviceError : RawPaymentFields.ReadError(capture.Body);
            return PaymentFromErrorStatus(request, ex.StatusCode, error, capture);
        }
        catch (ResponseDeserializationException ex)
        {
            // Adyen answered, but not in the shape the SDK expects. The verbatim body is in the capture; read the
            // documented fields from it rather than guessing.
            _logger.LogWarning("Adyen payment response for {Reference} did not match {TargetType} (HTTP {Status}).",
                request.MerchantReference, ex.TargetType.Name, (int)ex.StatusCode);
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                var raw = RawPaymentFields.Read(capture.Body);
                return raw.ResultCode is null
                    ? Unknown(capture, "Adyen's response could not be read.")
                    : MapPayment(request, raw.ResultCode, raw.PspReference, raw.RefusalReason, raw.RefusalReasonCode,
                        raw.AmountValue, raw.AmountCurrency, capture);
            }
            return PaymentFromErrorStatus(request, ex.StatusCode, RawPaymentFields.ReadError(capture.Body), capture);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning("Adyen payment {Reference}: no response within {Timeout}; outcome unknown.", request.MerchantReference, ex.Timeout);
            return Unknown(capture, $"No response from Adyen within {ex.Timeout.TotalSeconds:0} s.");
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning("Adyen payment {Reference}: connection failed ({Error}); outcome unknown.", request.MerchantReference, ex.InnerException?.Message ?? ex.Message);
            return Unknown(capture, $"Connection to Adyen failed: {ex.InnerException?.Message ?? "no details"}.");
        }
        catch (AuthSchemeException)
        {
            _logger.LogError("Adyen payment {Reference}: the API key could not be applied.", request.MerchantReference);
            return new CardPaymentResult(CardPaymentOutcome.Rejected, null, null, null, null, null,
                "Adyen credentials could not be applied.", true, Exchange(capture, "Credentials could not be applied."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Adyen payment {Reference}: deadline of {Deadline} reached; outcome unknown.", request.MerchantReference, _settings.Timeout + DeadlineGrace);
            return Unknown(capture, "The call to Adyen was abandoned at its deadline.");
        }
    }

    public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        var capture = new AdyenResponseCapture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_settings.Timeout + DeadlineGrace);
        try
        {
            var response = await _client.Modifications.RefundPayment(
                new RefundPaymentRequest
                {
                    PaymentPspReference = request.PaymentPspReference,
                    IdempotencyKey = request.IdempotencyKey,
                    Body = new PaymentRefundRequest
                    {
                        Amount = new Amount37 { Currency = request.Currency, Value = request.AmountMinor },
                        MerchantAccount = _settings.MerchantAccount!,
                        Reference = request.MerchantReference,
                    },
                },
                capture.RequestOptions,
                deadline.Token);

            return new RefundResult(RefundOutcome.Received, response.PspReference, null, null, false, Exchange(capture, null));
        }
        catch (ApiException<RefundPaymentError> ex)
        {
            // Statuses without a typed body fall to TryGetRawError; their body is already in the capture verbatim.
            var error = ex.Error.TryGetServiceError(out var serviceError) ? serviceError : RawPaymentFields.ReadError(capture.Body);
            return RefundFromErrorStatus(request, ex.StatusCode, error, capture);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogWarning("Adyen refund response for {Reference} did not match {TargetType} (HTTP {Status}).",
                request.MerchantReference, ex.TargetType.Name, (int)ex.StatusCode);
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                var raw = RawPaymentFields.Read(capture.Body);
                return raw.PspReference is null
                    ? UnknownRefund(capture, "Adyen's response could not be read.")
                    : new RefundResult(RefundOutcome.Received, raw.PspReference, null, null, false, Exchange(capture, null));
            }
            return RefundFromErrorStatus(request, ex.StatusCode, RawPaymentFields.ReadError(capture.Body), capture);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning("Adyen refund {Reference}: no response within {Timeout}; outcome unknown.", request.MerchantReference, ex.Timeout);
            return UnknownRefund(capture, $"No response from Adyen within {ex.Timeout.TotalSeconds:0} s.");
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning("Adyen refund {Reference}: connection failed ({Error}); outcome unknown.", request.MerchantReference, ex.InnerException?.Message ?? ex.Message);
            return UnknownRefund(capture, $"Connection to Adyen failed: {ex.InnerException?.Message ?? "no details"}.");
        }
        catch (AuthSchemeException)
        {
            _logger.LogError("Adyen refund {Reference}: the API key could not be applied.", request.MerchantReference);
            return new RefundResult(RefundOutcome.Rejected, null, null, "Adyen credentials could not be applied.", true,
                Exchange(capture, "Credentials could not be applied."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Adyen refund {Reference}: deadline reached; outcome unknown.", request.MerchantReference);
            return UnknownRefund(capture, "The call to Adyen was abandoned at its deadline.");
        }
    }

    private CardPaymentResult MapPayment(CardPaymentRequest request, string? resultCode, string? pspReference,
        string? refusalReason, string? refusalReasonCode, long? chargedValue, string? chargedCurrency, AdyenResponseCapture capture)
    {
        var outcome = resultCode switch
        {
            null => CardPaymentOutcome.Unknown,
            _ when resultCode == ResultCode1.Authorised.Value => CardPaymentOutcome.Authorised,
            _ when resultCode == ResultCode1.Refused.Value => CardPaymentOutcome.Refused,
            _ when resultCode == ResultCode1.Error.Value || resultCode == ResultCode1.Cancelled.Value => CardPaymentOutcome.Failed,
            _ when resultCode == ResultCode1.RedirectShopper.Value || resultCode == ResultCode1.IdentifyShopper.Value
                || resultCode == ResultCode1.ChallengeShopper.Value || resultCode == ResultCode1.PresentToShopper.Value
                => CardPaymentOutcome.ActionRequired,
            // Pending, Received, PartiallyAuthorised and anything this build does not know: money may be moving,
            // so the order is held (no second payment) until the outcome is settled.
            _ => CardPaymentOutcome.Pending,
        };

        if (outcome == CardPaymentOutcome.Authorised && chargedValue is not null
            && (chargedValue != request.AmountMinor || !string.Equals(chargedCurrency, request.Currency, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogError("Adyen authorised {Reference} for {ChargedValue} {ChargedCurrency} instead of {Expected} {Currency}; holding the order for review.",
                request.MerchantReference, chargedValue, chargedCurrency, request.AmountMinor, request.Currency);
            outcome = CardPaymentOutcome.Pending;
        }

        return new CardPaymentResult(outcome, pspReference, resultCode, refusalReason, refusalReasonCode, null, null, false,
            Exchange(capture, null));
    }

    private CardPaymentResult PaymentFromErrorStatus(CardPaymentRequest request, HttpStatusCode status, ServiceError? error,
        AdyenResponseCapture capture)
    {
        var code = (int)status;
        _logger.LogWarning("Adyen rejected payment {Reference}: HTTP {Status}, errorCode {ErrorCode}, errorType {ErrorType}, pspReference {PspReference}.",
            request.MerchantReference, code, error?.ErrorCode ?? "-", error?.ErrorType ?? "-", error?.PspReference ?? "-");

        if (IsAmbiguous(code))
            return new CardPaymentResult(CardPaymentOutcome.Unknown, error?.PspReference, null, null, null, error?.ErrorCode,
                error?.Message, false, Exchange(capture, null));

        return new CardPaymentResult(CardPaymentOutcome.Rejected, error?.PspReference, null, null, null, error?.ErrorCode,
            error?.Message, IsMerchantSide(code), Exchange(capture, null));
    }

    private RefundResult RefundFromErrorStatus(RefundRequest request, HttpStatusCode status, ServiceError? error,
        AdyenResponseCapture capture)
    {
        var code = (int)status;
        _logger.LogWarning("Adyen rejected refund {Reference}: HTTP {Status}, errorCode {ErrorCode}, errorType {ErrorType}, pspReference {PspReference}.",
            request.MerchantReference, code, error?.ErrorCode ?? "-", error?.ErrorType ?? "-", error?.PspReference ?? "-");

        if (IsAmbiguous(code))
            return new RefundResult(RefundOutcome.Unknown, null, error?.ErrorCode, error?.Message, false, Exchange(capture, null));

        return new RefundResult(RefundOutcome.Rejected, null, error?.ErrorCode, error?.Message, IsMerchantSide(code),
            Exchange(capture, null));
    }

    // A server-side failure or a request timeout may come after Adyen acted: settle by re-sending with the same key.
    private static bool IsAmbiguous(int status) => status >= 500 || status == 408;

    // Our credentials, permissions or quota — not something the shopper or operator can fix.
    private static bool IsMerchantSide(int status) => status is 401 or 403 or 429;

    private static CardPaymentResult Unknown(AdyenResponseCapture capture, string transportError) =>
        new(CardPaymentOutcome.Unknown, null, null, null, null, null, null, false, Exchange(capture, transportError));

    private static RefundResult UnknownRefund(AdyenResponseCapture capture, string transportError) =>
        new(RefundOutcome.Unknown, null, null, null, false, Exchange(capture, transportError));

    private static ProviderExchange Exchange(AdyenResponseCapture capture, string? transportError) =>
        new(capture.StatusCode, capture.Body, transportError);

    /// <summary>Reads Adyen's documented wire fields straight from a body the SDK model could not deserialize.</summary>
    private sealed record RawPaymentFields(string? ResultCode, string? PspReference, string? RefusalReason,
        string? RefusalReasonCode, long? AmountValue, string? AmountCurrency)
    {
        public static RawPaymentFields Read(string? body)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(body))
                    return new RawPaymentFields(null, null, null, null, null, null);
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                long? value = null;
                string? currency = null;
                if (root.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Object)
                {
                    if (amount.TryGetProperty("value", out var v) && v.TryGetInt64(out var parsed))
                        value = parsed;
                    currency = String(amount, "currency");
                }
                return new RawPaymentFields(String(root, "resultCode"), String(root, "pspReference"),
                    String(root, "refusalReason"), String(root, "refusalReasonCode"), value, currency);
            }
            catch (JsonException)
            {
                return new RawPaymentFields(null, null, null, null, null, null);
            }
        }

        public static ServiceError? ReadError(string? body)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(body))
                    return null;
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                return root.ValueKind != JsonValueKind.Object
                    ? null
                    : new ServiceError
                    {
                        ErrorCode = String(root, "errorCode"),
                        ErrorType = String(root, "errorType"),
                        Message = String(root, "message"),
                        PspReference = String(root, "pspReference"),
                    };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? String(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
    }
}
