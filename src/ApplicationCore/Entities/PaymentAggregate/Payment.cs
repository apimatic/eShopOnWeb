using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for a single <see cref="OrderAggregate.Order"/>. There is exactly one Payment per
/// order; it is created (awaiting payment) when the order is placed and then carries every piece
/// of state PayPal owns — the hold, the capture, and the refunds — so that a later request can act
/// on the payment, not only the one that started it. This is a separate aggregate root that
/// references the order by id, which keeps the existing <see cref="OrderAggregate.Order"/> model
/// untouched (an additive capability).
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<Refund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // --- State owned by PayPal ---------------------------------------------------------------
    /// <summary>
    /// The merchant invoice id sent to PayPal on the order. Globally unique (survives in-memory id
    /// resets) so it can line PayPal's transaction records up against this eShop order.
    /// </summary>
    public string? InvoiceId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Safe descriptors of the card used (never full details) ------------------------------
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total already refunded (only refunds PayPal has not failed/cancelled count).</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>Amount still available to refund against the capture.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Record the invoice id used on the PayPal order before authorizing.</summary>
    public void AssignInvoiceId(string invoiceId)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        InvoiceId = invoiceId;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Record a renewed authorization id/status after a stale one was reauthorized.</summary>
    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? paypalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
    }

    /// <summary>
    /// Register an intended refund and enforce that the running total never exceeds what was
    /// captured. Returns the created <see cref="Refund"/> so the caller can complete it once PayPal
    /// confirms. Throws if the amount would over-refund the capture.
    /// </summary>
    public Refund AddRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException(
                "A refund can only be issued for a payment that has been captured.");
        }

        if (amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} {CurrencyCode} exceeds the remaining refundable amount " +
                $"of {RefundableRemaining:0.00} {CurrencyCode}.");
        }

        var refund = new Refund(idempotencyKey, amount, CurrencyCode);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Recompute the payment status after a refund completes.</summary>
    public void ApplyRefundOutcome()
    {
        var refunded = TotalRefunded;
        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (refunded > 0m)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
