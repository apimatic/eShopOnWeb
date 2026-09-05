using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Aggregate holding the full payment state for an order: the PayPal identifiers and
/// statuses for the authorization (hold), the capture and the refunds, plus the local
/// lifecycle status. One payment per order (unique OrderId).
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;

    /// <summary>
    /// Unique key generated when the payment record is created and persisted before any
    /// provider call. It scopes every provider idempotency (PayPal-Request-Id) value for
    /// this payment so request ids never collide across orders or application restarts.
    /// </summary>
    public string PaymentKey { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }

    /// <summary>Number of authorization attempts made for this order (1-based).</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Identifier of the saved card used, when paid with one.</summary>
    public int? PaymentMethodId { get; private set; }

    // PayPal order / authorization state
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture state
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    private Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        OrderId = orderId;
        BuyerId = buyerId;
        PaymentKey = Guid.NewGuid().ToString("N");
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Authorizing;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Creates the payment record for the first authorization attempt.</summary>
    public static Payment CreateFirstAttempt(int orderId, string buyerId, decimal amount, string currency)
    {
        var payment = new Payment(orderId, buyerId, amount, currency);
        payment.AttemptCount = 1;
        return payment;
    }

    /// <summary>Starts a new authorization attempt after a previous failure.</summary>
    public void StartNewAttempt()
    {
        if (Status != PaymentStatus.Failed && Status != PaymentStatus.Authorizing)
        {
            throw new InvalidOperationException($"Cannot start a new payment attempt while the payment is {Status}.");
        }
        AttemptCount++;
        Status = PaymentStatus.Authorizing;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records a successful authorization (hold) of the funds.</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, int? paymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodId = paymentMethodId;
        Status = PaymentStatus.Authorized;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Updates the stored authorization state (e.g. after a reauthorization).</summary>
    public void ReplaceAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records a failed authorization attempt; funds were never held.</summary>
    public void MarkAttemptFailed()
    {
        Status = PaymentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records the voiding of the authorization: the hold was released, no money moved.</summary>
    public void MarkVoided(string authorizationStatus)
    {
        PayPalAuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks an order cancelled before any payment was made.</summary>
    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records the capture of the authorized funds at fulfilment.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount, string? authorizationStatus)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        if (authorizationStatus != null)
        {
            PayPalAuthorizationStatus = authorizationStatus;
        }
        Status = PaymentStatus.Captured;
        UpdatedAt = CapturedAt.Value;
    }

    /// <summary>Records a refund against the capture and updates the payment status.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        var refunded = RefundedAmountCommitted;
        var captured = CapturedAmount ?? Amount;
        Status = refunded >= captured ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Amount already committed to refunds (completed or pending).</summary>
    public decimal RefundedAmountCommitted =>
        _refunds.Where(r => r.Status == PaymentRefundStatus.Completed || r.Status == PaymentRefundStatus.Pending)
            .Sum(r => r.Amount);
}
