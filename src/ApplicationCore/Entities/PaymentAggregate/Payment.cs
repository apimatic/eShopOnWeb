using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A payment attempt for an order. Carries the PayPal-owned state (order id, authorization id/status/expiry,
/// capture id and money breakdown) so any later request can act on it, plus the idempotency keys used for
/// the PayPal calls so a retried request re-sends the same key instead of charging twice.
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
        UpdatedAt = CreatedAt;
        CreateRequestKey = NewRequestKey("create");
        AuthorizeRequestKey = NewRequestKey("authorize");
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // Idempotency keys sent as PayPal-Request-Id; persisted so a retry of the same logical
    // operation reuses the key and PayPal de-duplicates instead of executing twice.
    public string CreateRequestKey { get; private set; }
    public string AuthorizeRequestKey { get; private set; }
    public string? CaptureRequestKey { get; private set; }
    public string? VoidRequestKey { get; private set; }
    public string? ReauthorizeRequestKey { get; private set; }

    public string? DeclineReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status == PaymentRefundStatus.Completed || r.Status == PaymentRefundStatus.Pending)
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkPayPalOrderCreated(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        Touch();
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Pending)
        {
            throw new PaymentStateConflictException($"Payment {Id} cannot be marked authorized while in state {Status}.");
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    public void MarkDeclined(string? reason)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new PaymentStateConflictException($"Payment {Id} cannot be marked declined while in state {Status}.");
        }

        Status = PaymentStatus.Declined;
        DeclineReason = reason;
        Touch();
    }

    /// <summary>
    /// A declined or voided attempt can be paid again: clear the PayPal state and issue fresh
    /// idempotency keys so the retry is a new logical operation at PayPal.
    /// </summary>
    public void ResetForRetry()
    {
        if (Status != PaymentStatus.Declined && Status != PaymentStatus.Voided)
        {
            throw new PaymentStateConflictException($"Payment {Id} in state {Status} cannot be retried.");
        }

        PayPalOrderId = null;
        AuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizationExpiresAt = null;
        DeclineReason = null;
        Status = PaymentStatus.Pending;
        CreateRequestKey = NewRequestKey("create");
        AuthorizeRequestKey = NewRequestKey("authorize");
        Touch();
    }

    public void MarkAuthorizationRenewed(string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateConflictException($"Payment {Id} in state {Status} cannot be reauthorized.");
        }

        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public string EnsureCaptureRequestKey()
    {
        CaptureRequestKey ??= NewRequestKey("capture");
        return CaptureRequestKey;
    }

    public string EnsureVoidRequestKey()
    {
        VoidRequestKey ??= NewRequestKey("void");
        return VoidRequestKey;
    }

    public string EnsureReauthorizeRequestKey()
    {
        ReauthorizeRequestKey ??= NewRequestKey("reauthorize");
        return ReauthorizeRequestKey;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount, string captureStatus)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateConflictException($"Payment {Id} in state {Status} cannot be captured.");
        }

        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = captureStatus;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized && Status != PaymentStatus.Pending)
        {
            throw new PaymentStateConflictException($"Payment {Id} in state {Status} cannot be voided.");
        }

        Status = PaymentStatus.Voided;
        Touch();
    }

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentStateConflictException($"Payment {Id} in state {Status} cannot be refunded.");
        }
        if (amount > RefundableAmount)
        {
            throw new PaymentStateConflictException(
                $"Refund of {amount} exceeds the remaining refundable amount {RefundableAmount} on payment {Id}.");
        }
        if (_refunds.Any(r => r.IdempotencyKey == idempotencyKey))
        {
            throw new PaymentStateConflictException($"A refund with idempotency key '{idempotencyKey}' already exists on payment {Id}.");
        }

        var refund = new PaymentRefund(idempotencyKey, amount);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    public void ApplyRefundedStatus()
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            return;
        }

        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private static string NewRequestKey(string operation)
        => $"eshop-{operation}-{Guid.NewGuid():N}";

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
