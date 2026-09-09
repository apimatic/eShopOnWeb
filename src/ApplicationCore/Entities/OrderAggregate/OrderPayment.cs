using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment attached to an <see cref="Order"/>. Records the state that PayPal owns
/// — the ids and current status for the hold (authorization), the capture, and the
/// refunds — so that a later request can act on the payment, not only the one that
/// started it. Owned by the Order aggregate.
/// </summary>
public class OrderPayment // owned entity of Order
{
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? authorizationExpiresAt,
        string authorizeRequestId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiresAt = authorizationExpiresAt;
        AuthorizeRequestId = authorizeRequestId;
    }

    // --- Hold (authorization) ---
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>Stable idempotency key used for the authorize call (PayPal-Request-Id).</summary>
    public string AuthorizeRequestId { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Refunds ---
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public bool IsCaptured => CaptureId is not null;

    /// <summary>The sum of refunds that consumed captured balance.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.CountsTowardRefundedTotal).Sum(r => r.Amount);

    /// <summary>How much of the captured payment can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Replace the current hold with a freshly renewed one (reauthorization).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkAuthorizationStatus(string status) => AuthorizationStatus = status;

    public void RecordCapture(string captureId, string status, decimal grossAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public void MarkVoided() => AuthorizationStatus = "VOIDED";

    /// <summary>Find a previously-recorded refund for this idempotency key, if any.</summary>
    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Guards that a refund of <paramref name="amount"/> is legitimate before it is sent to PayPal:
    /// the payment must be captured and the amount must not exceed what remains refundable.
    /// </summary>
    public void GuardRefundable(decimal amount)
    {
        if (!IsCaptured)
            throw new PaymentConflictException("The order has not been fulfilled, so its payment cannot be refunded.");

        if (amount <= 0m)
            throw new PaymentValidationException("Refund amount must be a positive number.");

        // Rounding-safe comparison to the cent.
        if (decimal.Round(amount, 2) > decimal.Round(RefundableRemaining, 2))
        {
            throw new PaymentConflictException(
                $"Refund of {amount:0.00} {Currency} exceeds the refundable balance of {RefundableRemaining:0.00} {Currency} " +
                $"(captured {CapturedAmount:0.00}, already refunded {TotalRefunded:0.00}).");
        }
    }

    public OrderRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new OrderRefund(payPalRefundId, amount, Currency, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}
