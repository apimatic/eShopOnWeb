using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the payment/fulfilment state of an <see cref="OrderAggregate.Order"/>. This is an additive
/// aggregate: it references the order by id and never mutates the existing order/order-item model.
/// It carries the state PayPal owns (the ids and statuses for the hold, the capture and the refunds)
/// so a later request can act on it, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        Status = PaymentStatus.PendingPayment;
    }

    /// <summary>The eShop order this payment settles.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (matches <see cref="OrderAggregate.Order.BuyerId"/>).</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Order total; the authorized hold must equal this to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    // --- PayPal-owned state -------------------------------------------------

    /// <summary>PayPal order id created for this payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>Idempotency key used for the authorize call, so a retry never doubles the hold.</summary>
    public string? AuthorizeRequestId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>Idempotency key used for the capture call, so a retry never doubles the capture.</summary>
    public string? CaptureRequestId { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>What PayPal reported at capture: gross captured, PayPal's fee and net proceeds.</summary>
    public decimal? CapturedGrossAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, when the shopper paid with a vaulted card (informational).</summary>
    public int? SavedPaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    // --- Behaviour ----------------------------------------------------------

    /// <summary>
    /// Reserve the idempotency key for the authorize call. On the first attempt a new key is stored and
    /// returned; a later attempt (e.g. a retry after a failure) reuses the stored key so PayPal dedups.
    /// </summary>
    public string BeginAuthorization()
    {
        if (Status != PaymentStatus.PendingPayment)
        {
            throw new PaymentConflictException(
                $"Order {OrderId} cannot be authorized because its payment is '{Status}'.");
        }

        AuthorizeRequestId ??= $"auth-{OrderId}-{Guid.NewGuid():N}";
        return AuthorizeRequestId;
    }

    public void SetAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus,
        DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>True when the hold has passed its honor window and must be renewed before capture.</summary>
    public bool IsAuthorizationStale(DateTimeOffset now) =>
        AuthorizationExpiresAt.HasValue && AuthorizationExpiresAt.Value <= now;

    /// <summary>Record a renewed authorization (after a reauthorize) without changing lifecycle state.</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException(
                $"Order {OrderId} authorization cannot be renewed because its payment is '{Status}'.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>
    /// Reserve the idempotency key for the capture call. Returns the stored key on a retry so PayPal dedups.
    /// </summary>
    public string BeginCapture()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException(
                $"Order {OrderId} cannot be fulfilled because its payment is '{Status}', not 'Authorized'.");
        }

        CaptureRequestId ??= $"capture-{OrderId}-{Guid.NewGuid():N}";
        return CaptureRequestId;
    }

    public void SetCaptured(string captureId, string? captureStatus, decimal grossAmount,
        decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGrossAmount = grossAmount;
        PayPalFeeAmount = paypalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException(
                $"Order {OrderId} cannot be cancelled because its payment is '{Status}'. " +
                "Only an order whose funds are held (not yet captured) can be cancelled.");
        }

        Status = PaymentStatus.Cancelled;
    }

    /// <summary>Sum of the refunds recorded so far.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still available to refund against the capture.</summary>
    public decimal RefundableRemaining() => (CapturedGrossAmount ?? 0m) - TotalRefunded();

    /// <summary>Find an already-recorded refund for this idempotency key (idempotent replay).</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Validate a refund without mutating the aggregate, and resolve the concrete amount (a null request
    /// amount means the full remaining balance). Throws when the payment is not in a refundable state or
    /// the amount would push the cumulative refund beyond what was captured. Call this before contacting
    /// PayPal so an invalid refund never reaches the provider.
    /// </summary>
    public decimal EnsureCanRefund(decimal? amount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException(
                $"Order {OrderId} cannot be refunded because its payment is '{Status}'. " +
                "Only a captured (fulfilled) payment can be refunded.");
        }

        var remaining = RefundableRemaining();
        var refundAmount = amount ?? remaining;

        if (refundAmount <= 0m)
        {
            throw new PaymentConflictException(
                $"Order {OrderId} has no balance left to refund.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentConflictException(
                $"Refund of {refundAmount:0.00} {CurrencyCode} exceeds the {remaining:0.00} {CurrencyCode} " +
                $"still refundable on order {OrderId} (captured {CapturedGrossAmount:0.00}, " +
                $"already refunded {TotalRefunded():0.00}).");
        }

        return refundAmount;
    }

    /// <summary>
    /// Create and attach a refund, enforcing that the cumulative refunded amount never exceeds what was
    /// captured. A null amount means a full refund of the remaining balance.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal? amount)
    {
        var refundAmount = EnsureCanRefund(amount);
        var refund = new PaymentRefund(idempotencyKey, refundAmount);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Recompute Captured / PartiallyRefunded / Refunded from the refunds recorded so far.</summary>
    public void RecalculateRefundState()
    {
        if (Status == PaymentStatus.Cancelled || Status == PaymentStatus.PendingPayment
            || Status == PaymentStatus.Authorized)
        {
            return;
        }

        var totalRefunded = TotalRefunded();
        if (totalRefunded <= 0m)
        {
            Status = PaymentStatus.Captured;
        }
        else if (totalRefunded >= (CapturedGrossAmount ?? 0m))
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }
}
