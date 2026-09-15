using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment record for an eShop <see cref="OrderAggregate.Order"/>. It is created when the order is
/// placed (state <see cref="PaymentStatus.AwaitingPayment"/>) and carries enough of the state PayPal owns
/// (ids and current status for the hold, the capture, and each refund) that a later request can act on it.
/// This is an additive aggregate — it does not modify the existing Order/OrderItem model.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode, string invoiceReference)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(invoiceReference, nameof(invoiceReference));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        InvoiceReference = invoiceReference;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment; the caller's identity. Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Order total snapshot — the amount to hold, to the cent.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Stable reference sent to PayPal as invoice_id at authorization, used for reconciliation.</summary>
    public string InvoiceReference { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    // --- State PayPal owns -------------------------------------------------
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? CapturedFee { get; private set; }
    public decimal? CapturedNet { get; private set; }

    // --- Idempotency keys (generated before the first attempt, reused on retry) ---
    public string? AuthorizeIdempotencyKey { get; private set; }
    public string? CaptureIdempotencyKey { get; private set; }

    /// <summary>Saved card used to pay, when the shopper paid with a vaulted card.</summary>
    public int? PaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Returns a stable idempotency key for the authorize operation, generating and persisting it on first
    /// use so a retried authorize re-sends the same PayPal-Request-Id and never holds funds twice.
    /// </summary>
    public string EnsureAuthorizeIdempotencyKey()
    {
        AuthorizeIdempotencyKey ??= Guid.NewGuid().ToString("N");
        return AuthorizeIdempotencyKey;
    }

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, int? paymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodId = paymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed authorization id (reauthorization before capture).</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public string EnsureCaptureIdempotencyKey()
    {
        CaptureIdempotencyKey ??= Guid.NewGuid().ToString("N");
        return CaptureIdempotencyKey;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal? gross, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = gross;
        CapturedFee = fee;
        CapturedNet = net;
        Status = PaymentStatus.Fulfilled;
    }

    public void RecordVoid(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Cancelled;
    }

    /// <summary>Total already refunded against the capture.</summary>
    public decimal RefundedAmount() => _refunds.Sum(r => r.Amount);

    /// <summary>
    /// Amount still refundable: the captured amount minus what has already been refunded. Never negative.
    /// A partly-refunded order can never become refundable beyond what was captured.
    /// </summary>
    public decimal RefundableRemaining()
    {
        var captured = CapturedGross ?? 0m;
        var remaining = captured - RefundedAmount();
        return remaining > 0m ? remaining : 0m;
    }

    /// <summary>Finds a prior refund created under the same caller-supplied idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundedAmount() >= (CapturedGross ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }

    public void MarkFailed() => Status = PaymentStatus.Failed;
}
