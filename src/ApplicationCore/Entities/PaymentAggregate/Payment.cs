using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Holds the payment state that PayPal owns for a single order: the ids and current status of the
/// hold (authorization), the capture, and any refunds — enough that a later request can act on the
/// payment, not only the request that started it. No card details are ever stored here.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment (the buyer identity, i.e. the user name from the token).</summary>
    public string BuyerId { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The amount authorized — equal to the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.Created;

    // PayPal-owned identifiers and their last-known status strings.
    public string PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    // Capture financials as reported by PayPal.
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currency, decimal amount, string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
        PayPalOrderId = payPalOrderId;
    }

    /// <summary>Record that funds are now held (authorized).</summary>
    public void SetAuthorized(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Record a renewed authorization (reauthorization) that replaces the stale one.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentDomainException(
                $"Payment for order {OrderId} cannot be reauthorized from status {Status}.");
        }
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Touch();
    }

    public void MarkFailed(string? reason = null)
    {
        AuthorizationStatus = reason ?? AuthorizationStatus;
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>Record the capture and what PayPal reported: captured amount, fee, and net proceeds.</summary>
    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void Void()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentDomainException(
                $"Payment for order {OrderId} cannot be voided from status {Status}; only an authorized (uncaptured) payment can be voided.");
        }
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
        Touch();
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Ensures a partial (or full) refund of <paramref name="amount"/> would not exceed the captured
    /// amount. Call before contacting PayPal so an over-refund never leaves the app.
    /// </summary>
    public void EnsureRefundable(decimal amount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentDomainException(
                $"Payment for order {OrderId} cannot be refunded from status {Status}; it must be captured first.");
        }
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining())
        {
            throw new PaymentDomainException(
                $"Refund of {amount} {Currency} exceeds the refundable remaining of {RefundableRemaining()} {Currency} for order {OrderId}.");
        }
    }

    /// <summary>Record a completed refund and advance the payment status accordingly.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
