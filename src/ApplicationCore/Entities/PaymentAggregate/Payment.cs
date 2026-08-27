using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment for an Order. Carries the identifiers and current status of everything
/// PayPal owns (order, authorization/hold, capture, refunds) so any later request
/// can act on the payment, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        InvoiceId = $"eshop-order-{orderId}-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>
    /// Merchant-unique invoice id sent to PayPal on the order, capture and refunds;
    /// appears in PayPal's transaction reports and is the reconciliation key.
    /// </summary>
    public string InvoiceId { get; private set; }

    /// <summary>The order total this payment must hold/capture, to the cent.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // PayPal order (created when the shopper pays)
    public string? PayPalOrderId { get; private set; }

    // Authorization (the hold)
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

    // Capture (money taken at fulfilment), as reported by PayPal
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status == "COMPLETED" || r.Status == "PENDING")
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void SetPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    public void MarkAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed (reauthorized) hold, which may carry a new authorization id.</summary>
    public void MarkAuthorizationRenewed(string authorizationId, string authorizationStatus, DateTimeOffset? expirationTime)
    {
        MarkAuthorized(authorizationId, authorizationStatus, expirationTime);
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
            throw new PaymentStateException($"Only an authorized payment can be voided (current status: {Status}).");
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
            throw new PaymentStateException($"Only an authorized payment can be captured (current status: {Status}).");

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
            throw new PaymentStateException($"Only a captured payment can be refunded (current status: {Status}).");
        if (amount <= 0m)
            throw new PaymentStateException("Refund amount must be positive.");
        if (amount > RefundableAmount)
            throw new PaymentStateException(
                $"Refund amount {amount:0.00} {Currency} exceeds the remaining refundable amount {RefundableAmount:0.00} {Currency} " +
                $"(captured {CapturedAmount:0.00}, already refunded {TotalRefunded:0.00}).");

        var refund = new PaymentRefund(Id, idempotencyKey, amount, Currency, noteToPayer);
        _refunds.Add(refund);
        return refund;
    }

    public void ApplySettledRefund(PaymentRefund refund, string payPalRefundId, string refundStatus)
    {
        refund.MarkSettled(payPalRefundId, refundStatus);
        if (RefundableAmount <= 0m)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (TotalRefunded > 0m)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
        CaptureStatus = Status == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
    }
}
