using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money movement and fulfilment state for one eShop <c>Order</c>. This aggregate is additive:
/// it references the existing order by id and carries everything PayPal owns (the hold, the
/// capture, the refunds) so a later request can act on it, not only the one that started it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        InvoiceId = invoiceId;
        Status = PaymentStatus.PendingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        // Stable, per-order idempotency keys — reused across retries so a double-click never
        // authorizes, captures, or voids twice at PayPal.
        AuthorizeIdempotencyKey = $"auth-{invoiceId}";
        CaptureIdempotencyKey = $"cap-{invoiceId}";
        VoidIdempotencyKey = $"void-{invoiceId}";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>Stable external invoice id (also sent to PayPal) used to reconcile transactions.</summary>
    public string InvoiceId { get; private set; }

    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // PayPal-owned identifiers and current state.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // Stable idempotency keys.
    public string AuthorizeIdempotencyKey { get; private set; }
    public string CaptureIdempotencyKey { get; private set; }
    public string VoidIdempotencyKey { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void MarkAuthorized(string payPalOrderId, string authorizationId, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Update the held authorization after a re-authorization renewed a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    /// <summary>The authorization is stale (past its expiry) and must be renewed before capture.</summary>
    public bool IsAuthorizationStale(DateTimeOffset now) =>
        AuthorizationExpiresAt.HasValue && now >= AuthorizationExpiresAt.Value;

    public decimal TotalRefunded() => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        var refund = new PaymentRefund(idempotencyKey, amount, CurrencyCode, payPalRefundId, status);
        _refunds.Add(refund);
        RecomputeRefundStatus();
        return refund;
    }

    public void RecomputeRefundStatus()
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            return;
        }

        var refunded = TotalRefunded();
        if (refunded <= 0m)
        {
            Status = PaymentStatus.Captured;
        }
        else if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }
}
