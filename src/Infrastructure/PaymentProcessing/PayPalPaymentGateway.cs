using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// PayPal-backed <see cref="IPaymentGateway"/>. All contract facts (operation signatures, wire
/// field names, enum values, error accessors) come from the grounded PayPal SDK contract sheet.
/// Every provider failure is translated into <see cref="PaymentException"/> with a caller-safe
/// message; card details are never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const string CurrencyDecimalFormat = "0.00";

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<PaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var card = request.Card;
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest
            {
                Name = card.CardholderName,
                Number = card.Number,
                Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                SecurityCode = card.SecurityCode,
                BillingAddress = ToSdkAddress(card.BillingAddress)
            }
        };

        return CreateAndCaptureAsync(request.ReferenceId, request.Amount, request.Currency, paymentSource, request.IdempotencyKey, cancellationToken);
    }

    public Task<PaymentResult> ChargeSavedCardAsync(SavedCardPaymentRequest request, CancellationToken cancellationToken = default)
    {
        // Charging a saved card references the vault token — never re-sends raw card details.
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest
            {
                VaultId = request.VaultTokenId
            }
        };

        return CreateAndCaptureAsync(request.ReferenceId, request.Amount, request.Currency, paymentSource, request.IdempotencyKey, cancellationToken);
    }

    private async Task<PaymentResult> CreateAndCaptureAsync(
        string referenceId,
        decimal amount,
        string currency,
        PaymentSource paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Capture,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = referenceId,
                    CustomId = referenceId,
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(amount)
                    }
                }
            },
            PaymentSource = paymentSource
        };

        Order order;
        var status = NewStatusScope();
        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: cancellationToken);

            // A CAPTURE-intent order with a card source may already be complete; only capture if not.
            if (created.Status == OrderStatus.Completed)
            {
                order = created;
            }
            else
            {
                var payPalOrderId = created.Id;
                if (string.IsNullOrEmpty(payPalOrderId))
                {
                    throw new PaymentException("The payment provider did not return an order id.", PaymentFailureKind.ProviderError);
                }

                status = NewStatusScope();
                order = await _client.Orders.CaptureOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
        }
        catch (SdkException<CreateOrderError> ex)
        {
            ex.Error.TryGetError(out Error typed);
            ex.Error.TryGetRawError(out RawError raw);
            throw TranslateOrdersError(typed, raw, status, "create the payment", ex);
        }
        catch (SdkException<CaptureOrderError> ex)
        {
            ex.Error.TryGetError(out Error typed);
            ex.Error.TryGetRawError(out RawError raw);
            throw TranslateOrdersError(typed, raw, status, "capture the payment", ex);
        }
        catch (PayPalRequestAlreadySentException ex)
        {
            throw NotConfirmedError("process the payment", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonException(status, "process the payment", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw TransportError(ex);
        }
        finally
        {
            PayPalResponseContext.Current = null;
        }

        return ReadCaptureResult(order);
    }

    public async Task<VaultedCard> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default)
    {
        var card = request.Card;
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToSdkAddress(card.BillingAddress)
                }
            }
        };

        var status = NewStatusScope();
        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: request.IdempotencyKey,
                body: body,
                ct: cancellationToken);

            var tokenId = token.Id;
            if (string.IsNullOrEmpty(tokenId))
            {
                throw new PaymentException("The payment provider did not return a saved-card token.", PaymentFailureKind.ProviderError);
            }

            var cardEntity = token.PaymentSource?.Card;
            return new VaultedCard(
                tokenId,
                cardEntity?.Brand?.Value, // .Value is the raw wire brand, e.g. "VISA"
                cardEntity?.LastDigits,
                cardEntity?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVaultError(ex.Error, status, "save the card", ex);
        }
        catch (PayPalRequestAlreadySentException ex)
        {
            throw NotConfirmedError("save the card", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonException(status, "save the card", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw TransportError(ex);
        }
        finally
        {
            PayPalResponseContext.Current = null;
        }
    }

    public async Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var status = NewStatusScope();
        try
        {
            // Full refund = empty body.
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: request.CaptureId,
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);

            var refundId = refund.Id;
            if (string.IsNullOrEmpty(refundId))
            {
                throw new PaymentException("The payment provider did not return a refund id.", PaymentFailureKind.ProviderError);
            }

            var refundStatus = refund.Status?.Value;
            if (refund.Status == RefundStatus.Cancelled || refund.Status == RefundStatus.Failed)
            {
                throw new PaymentException($"The refund was not completed (status {refundStatus}).", PaymentFailureKind.Rejected);
            }

            return new RefundResult(refundId, refundStatus ?? RefundStatus.Completed.Value);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateRefundError(ex.Error, status, ex);
        }
        catch (PayPalRequestAlreadySentException ex)
        {
            throw NotConfirmedError("refund the payment", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonException(status, "refund the payment", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw TransportError(ex);
        }
        finally
        {
            PayPalResponseContext.Current = null;
        }
    }

    private PaymentResult ReadCaptureResult(Order order)
    {
        // Every hop is nullable and only populated when prefer=return=representation was sent.
        var capture = order.PurchaseUnits?
            .FirstOrDefault()?
            .Payments?
            .Captures?
            .FirstOrDefault();

        var captureId = capture?.Id;
        if (string.IsNullOrEmpty(captureId))
        {
            throw new PaymentException("The payment provider did not return a capture id.", PaymentFailureKind.ProviderError);
        }

        if (capture!.Status != CaptureStatus.Completed)
        {
            var captureStatus = capture.Status?.Value ?? "UNKNOWN";
            throw new PaymentException($"The payment was not completed (status {captureStatus}).", PaymentFailureKind.Rejected);
        }

        var payPalOrderId = order.Id ?? string.Empty;
        return new PaymentResult(payPalOrderId, captureId!, CaptureStatus.Completed.Value);
    }

    // --- Error translation -------------------------------------------------------------------

    private PaymentException TranslateOrdersError(Error? typed, RawError? raw, PayPalResponseStatus status, string action, Exception inner)
    {
        if (typed is not null)
        {
            var issues = typed.Details?.Select(d => d.Issue) ?? Enumerable.Empty<string>();
            return BuildTypedError(typed.Name, typed.Message, typed.DebugId, issues, status, action, inner);
        }

        if (raw is not null)
        {
            return BuildRawError(raw, action, inner);
        }

        return UnknownProviderError(action, inner);
    }

    private PaymentException TranslateRefundError(RefundCapturedPaymentError error, PayPalResponseStatus status, Exception inner)
    {
        if (error.TryGetError(out Error typed))
        {
            var issues = typed.Details?.Select(d => d.Issue) ?? Enumerable.Empty<string>();
            return BuildTypedError(typed.Name, typed.Message, typed.DebugId, issues, status, "refund the payment", inner);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return BuildRawError(noContent, "refund the payment", inner);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return BuildRawError(raw, "refund the payment", inner);
        }

        return UnknownProviderError("refund the payment", inner);
    }

    private PaymentException TranslateVaultError(CreatePaymentTokenError error, PayPalResponseStatus status, string action, Exception inner)
    {
        if (error.TryGetError1(out Error1 typed))
        {
            var issues = typed.Details?.Select(d => d.Issue) ?? Enumerable.Empty<string>();
            return BuildTypedError(typed.Name, typed.Message, typed.DebugId, issues, status, action, inner);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return BuildRawError(raw, action, inner);
        }

        return UnknownProviderError(action, inner);
    }

    private PaymentException BuildTypedError(
        string? name,
        string? message,
        string? debugId,
        IEnumerable<string?> issues,
        PayPalResponseStatus status,
        string action,
        Exception inner)
    {
        var issueList = issues.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i!).ToList();
        var safeMessage = Compose(name, message, issueList, action);

        // Typed (mapped) errors are the documented 4xx business/validation rejections; the raw
        // HTTP status, when the handler captured it, refines rejection vs provider-side fault.
        var kind = ClassifyStatus(status.StatusCode, defaultKind: PaymentFailureKind.Rejected);

        _logger.LogWarning(inner,
            "PayPal failed to {Action}. name={Name} debugId={DebugId} status={Status} issues={Issues}",
            action, name, debugId, status.StatusCode, string.Join("; ", issueList));

        return new PaymentException(safeMessage, kind, inner);
    }

    private PaymentException BuildRawError(RawError raw, string action, Exception inner)
    {
        var kind = ClassifyStatus(raw.StatusCode, defaultKind: PaymentFailureKind.ProviderError);

        _logger.LogWarning(inner, "PayPal failed to {Action}. status={Status}", action, raw.StatusCode);

        var safeMessage = kind == PaymentFailureKind.Rejected
            ? $"The payment provider rejected the request to {action}."
            : $"The payment provider was unable to {action}. Please try again later.";

        return new PaymentException(safeMessage, kind, inner);
    }

    private PaymentException TranslateJsonException(PayPalResponseStatus status, string action, JsonException inner)
    {
        // A JsonException can mean either an unreadable 2xx (outcome unknown) or an error body that
        // did not match its typed model (a rejection whose status was destroyed with the exception).
        // The captured status disambiguates; without it, default to a provider fault.
        var kind = ClassifyStatus(status.StatusCode, defaultKind: PaymentFailureKind.ProviderError);

        _logger.LogError(inner, "PayPal returned a response that could not be processed while trying to {Action}. status={Status}",
            action, status.StatusCode);

        var safeMessage = kind == PaymentFailureKind.Rejected
            ? $"The payment provider rejected the request to {action}."
            : $"The payment provider returned a response that could not be processed while trying to {action}.";

        return new PaymentException(safeMessage, kind, inner);
    }

    private PaymentException TransportError(Exception inner)
    {
        _logger.LogError(inner, "The payment provider was unreachable.");
        return new PaymentException("The payment provider is currently unreachable. Please try again later.", PaymentFailureKind.ProviderError, inner);
    }

    private PaymentException NotConfirmedError(string action, Exception inner)
    {
        // The single send failed in transit and was not resent, so we cannot confirm the outcome.
        // Surfacing this (rather than resending) is what prevents a duplicate charge/refund.
        _logger.LogError(inner, "PayPal request to {Action} could not be confirmed and was not resent.", action);
        return new PaymentException(
            $"The request to {action} could not be confirmed. Check the order's status before retrying.",
            PaymentFailureKind.ProviderError,
            inner);
    }

    /// <summary>
    /// Opens a fresh per-request scope on the current async flow: one permitted network send, and a
    /// slot for the response status. Call immediately before each individual SDK operation.
    /// </summary>
    private static PayPalResponseStatus NewStatusScope()
    {
        var status = new PayPalResponseStatus();
        PayPalResponseContext.Current = status;
        return status;
    }

    private PaymentException UnknownProviderError(string action, Exception inner)
    {
        _logger.LogError(inner, "PayPal returned an unrecognised error while trying to {Action}.", action);
        return new PaymentException($"The payment provider was unable to {action}. Please try again later.", PaymentFailureKind.ProviderError, inner);
    }

    private static PaymentFailureKind ClassifyStatus(HttpStatusCode? statusCode, PaymentFailureKind defaultKind)
    {
        if (statusCode is null)
        {
            return defaultKind;
        }

        var code = (int)statusCode.Value;

        // 401/403 are our own credential/permission problems — not caller-actionable.
        if (code == 401 || code == 403)
        {
            return PaymentFailureKind.ProviderError;
        }

        if (code >= 400 && code < 500)
        {
            return PaymentFailureKind.Rejected;
        }

        return PaymentFailureKind.ProviderError;
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is HttpRequestException)
        {
            return true;
        }

        // The SDK's per-attempt timeout throws TaskCanceledException without our token being set;
        // a caller-initiated cancellation should propagate instead of becoming a provider error.
        if (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        return false;
    }

    // --- Mapping helpers ---------------------------------------------------------------------

    private static Address ToSdkAddress(BillingAddress billing) => new Address
    {
        AddressLine1 = billing.AddressLine1,
        AddressLine2 = billing.AddressLine2,
        AdminArea2 = billing.City,
        AdminArea1 = billing.State,
        PostalCode = billing.PostalCode,
        CountryCode = billing.CountryCode
    };

    private static string FormatAmount(decimal amount) =>
        amount.ToString(CurrencyDecimalFormat, CultureInfo.InvariantCulture);

    private static string FormatExpiry(int year, int month) =>
        string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}", year, month);

    private static string Compose(string? name, string? message, IReadOnlyCollection<string> issues, string action)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message!);
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add(name!);
        }

        if (issues.Count > 0)
        {
            parts.Add(string.Join("; ", issues));
        }

        return parts.Count > 0
            ? $"The payment provider rejected the request to {action}: {string.Join(" - ", parts)}."
            : $"The payment provider rejected the request to {action}.";
    }
}
