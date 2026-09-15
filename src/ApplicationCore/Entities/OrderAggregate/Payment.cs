using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money side of an <see cref="Order"/>. A Payment carries enough of the state PayPal owns
/// (the ids and current status of the hold, the capture and each refund) that a later request can
/// act on it, not only the request that started it. It is part of the Order aggregate: it is only
/// ever created and mutated through payment operations on the order.
/// </summary>
public class Payment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(string reference, decimal amount, string currency, string payPalOrderId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        Reference = reference;
        Amount = amount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        IdempotencyKey = idempotencyKey;
        Status = PaymentStatus.Authorized;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The unique reference we send to PayPal as the order's invoice_id, used for reconciliation.</summary>
    public string Reference { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Root idempotency token; per-operation PayPal-Request-Id keys are derived from it.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // --- PayPal-owned state: the hold ---
    public string PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- PayPal-owned state: the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- PayPal-owned state: the refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed (reauthorized) hold. PayPal may return a new authorization id.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void SetCapture(string captureId, string captureStatus, decimal gross, decimal payPalFee, decimal net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = gross;
        PayPalFee = payPalFee;
        NetAmount = net;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Sum of refunds that actually returned (or will return) money.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.ReducesRefundableBalance).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded. Never negative.</summary>
    public decimal RefundableRemaining => Math.Max(0m, (CapturedGross ?? 0m) - TotalRefunded);

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(refundId, amount, Currency, status, idempotencyKey);
        _refunds.Add(refund);

        if (RefundableRemaining <= 0m)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (TotalRefunded > 0m)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }

        return refund;
    }
}
