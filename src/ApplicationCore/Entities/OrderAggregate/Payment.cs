using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment state for an order. Holds the identifiers and statuses PayPal owns
/// (order, authorization, capture, refunds) so any later request can act on them.
/// Never carries full card details.
/// </summary>
public class Payment : BaseEntity
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
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>PayPal Orders v2 order id.</summary>
    public string? PayPalOrderId { get; private set; }
    /// <summary>Current PayPal authorization id (changes when reauthorized).</summary>
    public string? AuthorizationId { get; private set; }
    /// <summary>Last status PayPal reported for the authorization (CREATED, VOIDED, ...).</summary>
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    /// <summary>
    /// Number of authorization attempts known to have failed. The gateway idempotency key is
    /// derived from this counter, so a retry after a lost response replays the same gateway
    /// request while a retry after a decline uses a fresh one.
    /// </summary>
    public int FailedAuthorizeAttempts { get; private set; }

    public string? CaptureId { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    /// <summary>Fee PayPal kept from the capture.</summary>
    public decimal? PayPalFee { get; private set; }
    /// <summary>Net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>Safe display text such as "VISA x-1111". Never full card details.</summary>
    public string? CardDescription { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status == RefundStatus.Completed)
        .Sum(r => r.Amount);

    /// <summary>What can still be refunded; never more than was captured.</summary>
    public decimal RefundableAmount => CapturedAmount.HasValue
        ? CapturedAmount.Value - TotalRefunded
        : 0m;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? cardDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardDescription = cardDescription;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed()
    {
        if (Status != PaymentStatus.Authorized)
        {
            FailedAuthorizeAttempts++;
            Status = PaymentStatus.AuthorizationFailed;
        }
    }

    /// <summary>Replaces the authorization after a successful reauthorization (PayPal issues a new id).</summary>
    public void RenewAuthorization(string newAuthorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Cannot renew an authorization while the payment is {Status}.");
        }

        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkVoided(string? authorizationStatus)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Only an authorized payment can be voided (current: {Status}).");
        }

        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = PaymentStatus.Voided;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? fee, decimal? net, DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Only an authorized payment can be captured (current: {Status}).");
        }

        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt;
        Status = PaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentStateException($"Only a captured payment can be refunded (current: {Status}).");
        }
        if (amount <= 0 || amount > RefundableAmount)
        {
            throw new PaymentStateException(
                $"Refund amount {amount} exceeds the refundable remainder {RefundableAmount} of the captured payment.");
        }

        var refund = new PaymentRefund(Id, idempotencyKey, amount, Currency, noteToPayer);
        _refunds.Add(refund);
        return refund;
    }

    internal void OnRefundCompleted()
    {
        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}
