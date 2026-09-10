using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the money movement for one <see cref="OrderAggregate.Order"/>. It is an aggregate root in its
/// own right (referencing the order by id) so the existing order/order-item model is reused unchanged.
/// It carries enough PayPal-owned state — the ids and current status of the hold, the capture and the
/// refunds — that a later request can act on the payment, not only the request that started it.
/// Full card details are never stored here.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper who owns this payment (and its order).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>A merchant-side reference unique per order, echoed by PayPal so records can be reconciled.</summary>
    public string? InvoiceId { get; private set; }

    // --- PayPal-owned state for the hold ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- PayPal-owned state for the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>How the shopper paid, described safely (e.g. "card ending 1111"). Never full details.</summary>
    public string? InstrumentDescription { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    /// <summary>Assigns the reconciliation invoice id at order-placement time (before any PayPal call).</summary>
    public void AssignInvoiceId(string invoiceId)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        InvoiceId = invoiceId;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? instrumentDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        InstrumentDescription = instrumentDescription;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces the authorization after a stale one was renewed (reauthorization).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>The amount still refundable — captured minus what has already been refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    public bool TryGetExistingRefund(string idempotencyKey, out PaymentRefund? refund)
    {
        refund = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        return refund is not null;
    }

    /// <summary>
    /// Records a refund. Enforces that cumulative refunds never exceed the captured amount, so a
    /// partly-refunded order can never become refundable beyond what was captured.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException(
                $"Order {OrderId} has no captured payment to refund (status: {Status}).");
        }

        if (RefundedAmount + refund.Amount > (CapturedAmount ?? 0m) + 0.001m)
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount:0.00} {Currency} would exceed the captured amount " +
                $"({CapturedAmount:0.00} {Currency}); already refunded {RefundedAmount:0.00}.");
        }

        _refunds.Add(refund);
        Status = RefundedAmount >= (CapturedAmount ?? 0m) - 0.001m
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
