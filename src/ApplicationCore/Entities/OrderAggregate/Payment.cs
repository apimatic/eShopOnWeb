using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment for an <see cref="Order"/>. Carries the state PayPal owns — the ids and current status of the
/// hold (authorization), the capture, and any refunds — so a later request can act on it. This is a child of
/// the Order aggregate; it is only ever loaded and mutated through its owning <see cref="Order"/>.
/// </summary>
public class Payment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(
        decimal amount,
        string currency,
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt,
        string? cardBrand,
        string? cardLastDigits,
        bool usedSavedCard)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        Amount = amount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        CardBrand = cardBrand;
        CardLastDigits = cardLastDigits;
        UsedSavedCard = usedSavedCard;
        Status = PaymentStatus.Authorized;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    public PaymentStatus Status { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    // ---- The hold (authorization) ----
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ---- The capture ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // ---- Card descriptor (safe, non-sensitive) ----
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }
    public bool UsedSavedCard { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total already refunded (counts completed and pending refunds so we never over-refund).</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.IsSuccessful).Sum(r => r.Amount);

    /// <summary>How much of the captured amount is still refundable.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Replace the authorization after a re-authorization (a new hold with a new id and honor period).</summary>
    public void ApplyReauthorization(string newAuthorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException("Only an authorized payment can be re-authorized.");

        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void UpdateAuthorizationStatus(string status) => AuthorizationStatus = status;

    /// <summary>Record a completed capture and the fee breakdown PayPal reported.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException("Only an authorized payment can be captured.");

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    /// <summary>Record that the authorization was voided (order cancelled before fulfilment).</summary>
    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException("Only an authorized payment can be voided.");

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Attach a refund and recompute the payment status.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        RecomputeRefundState();
    }

    public void RecomputeRefundState()
    {
        if (CapturedAmount is null) return;
        var refunded = TotalRefunded;
        if (refunded <= 0m)
            Status = PaymentStatus.Captured;
        else if (refunded >= CapturedAmount.Value)
            Status = PaymentStatus.Refunded;
        else
            Status = PaymentStatus.PartiallyRefunded;
    }
}
