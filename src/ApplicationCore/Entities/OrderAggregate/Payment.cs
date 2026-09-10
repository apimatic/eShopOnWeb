using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-backed payment for an <see cref="Order"/>. It carries enough of the state PayPal owns
/// (the ids and current status of the hold, the capture and each refund) that a later request can act
/// on it — capture the hold, void it, or refund the capture — without depending on the request that
/// created it. It is part of the Order aggregate; callers mutate it through the owning <see cref="Order"/>.
/// </summary>
public class Payment : BaseEntity
{
    public string Currency { get; private set; }

    /// <summary>The order total that was authorized, to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- State owned by PayPal ---

    /// <summary>The PayPal Orders v2 order id.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>The PayPal authorization (hold) id.</summary>
    public string AuthorizationId { get; private set; }

    /// <summary>PayPal's current status for the authorization (e.g. CREATED, CAPTURED, VOIDED, EXPIRED).</summary>
    public string AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization expires; used to decide whether it must be re-authorized before capture.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>The amount PayPal actually captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's processing fee for the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>The net proceeds credited to the merchant (gross minus fee).</summary>
    public decimal? NetAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, decimal authorizedAmount, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the authorization after it was renewed (re-authorized) because the previous hold went stale.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>The sum of all refunds recorded against the capture.</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured payment remains refundable.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Returns the refund previously recorded under <paramref name="idempotencyKey"/>, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}
