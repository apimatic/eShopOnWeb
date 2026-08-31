using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the PayPal-owned state of the money movement for one order: the hold
/// (authorization), the capture taken at fulfilment, and any refunds. Ids and
/// statuses stored here let any later request act on the payment, not only the
/// request that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.PendingAuthorization;
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>The order total this payment authorizes, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    // PayPal order / authorization (the hold)
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture (money taken at fulfilment)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Idempotency key sent to PayPal for the authorize call (PayPal-Request-Id).</summary>
    public string AuthorizeRequestId { get; private set; }
    /// <summary>Idempotency key sent to PayPal for the capture call (PayPal-Request-Id).</summary>
    public string CaptureRequestId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status != "FAILED" && r.Status != "CANCELLED")
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    public void MarkAuthorizationFailed(string? payPalOrderId, string? authorizationStatus)
    {
        PayPalOrderId = payPalOrderId ?? PayPalOrderId;
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.AuthorizationFailed;
        Touch();
    }

    public void MarkReauthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    public void MarkVoided()
    {
        if (Status == PaymentStatus.Voided)
        {
            return;
        }
        if (Status != PaymentStatus.Authorized && Status != PaymentStatus.PendingAuthorization)
        {
            throw new PaymentException($"Payment {Id} cannot be voided while in state {Status}.");
        }
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public PaymentRefund AddRefund(string payPalRefundId, string refundStatus, decimal amount, string idempotencyKey, string? noteToPayer)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Payment {Id} is not captured; nothing can be refunded.");
        }

        var existing = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (amount <= 0 || amount > RefundableAmount)
        {
            throw new PaymentException(
                $"Refund of {amount:0.00} {Currency} exceeds the refundable balance of {RefundableAmount:0.00} {Currency} " +
                $"(captured {CapturedAmount:0.00}, already refunded {TotalRefunded:0.00}).");
        }

        var refund = new PaymentRefund(payPalRefundId, refundStatus, amount, idempotencyKey, noteToPayer);
        _refunds.Add(refund);

        Status = RefundableAmount <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
