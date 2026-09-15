using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money side of an <see cref="Order"/>. Holds enough of the state PayPal owns
/// (ids and current status for the hold, the capture and the refunds) that a later
/// request can act on it, not only the one that started it. This entity never stores
/// raw card details.
/// </summary>
public class Payment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(string currency, decimal amount)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Currency = currency;
        Amount = amount;
        Status = PaymentStatus.Pending;
    }

    /// <summary>Currency the order is charged in (from configuration).</summary>
    public string Currency { get; private set; }

    /// <summary>The authorized amount — equal to the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- The hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- The capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Which saved card (if any) paid this ---
    public int? PaymentMethodId { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of refunds that are in-flight or settled (failed refunds excluded).</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.IsActive).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded. Never negative.</summary>
    public decimal RefundableAmount =>
        Math.Max(0m, (CapturedAmount ?? 0m) - TotalRefunded);

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

    /// <summary>
    /// A reauthorization replaces the current hold with a fresh one (a new authorization id)
    /// when the original went stale before fulfilment.
    /// </summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public Refund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Registers a pending refund after validating it against the refundable balance.
    /// A partly-refunded order can never become refundable beyond what was captured.
    /// </summary>
    public Refund AddRefund(string idempotencyKey, decimal amount)
    {
        if (Status != PaymentStatus.Captured &&
            Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }

        if (amount > RefundableAmount)
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} {Currency} exceeds the refundable balance of {RefundableAmount:0.00} {Currency}.");
        }

        var refund = new Refund(idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Recomputes the payment status from settled refunds after a refund succeeds.</summary>
    public void ApplyRefundSettlement()
    {
        var refunded = _refunds.Where(r => r.IsActive).Sum(r => r.Amount);
        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (refunded > 0m)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }
}
