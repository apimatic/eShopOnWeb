using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state for an order. Carries the identifiers and statuses the payment
/// provider owns (PayPal order / authorization / capture / refunds) so that any
/// later request can act on the payment, not only the one that started it.
/// Never carries card details.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
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
    public int AuthorizationAttempts { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Human/actionable reason for the last decline or renewal failure.</summary>
    public string? LastFailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public string NextAuthorizationRequestId() => $"eshop-order{OrderId}-auth-{AuthorizationAttempts + 1}";

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationAttempts++;
        Status = PaymentStatus.Authorized;
        LastFailureReason = null;
        Touch();
    }

    public void RecordAuthorizationFailure(string? payPalOrderId, string reason)
    {
        if (!string.IsNullOrEmpty(payPalOrderId))
        {
            PayPalOrderId = payPalOrderId;
        }
        AuthorizationAttempts++;
        Status = PaymentStatus.AuthorizationFailed;
        LastFailureReason = reason;
        Touch();
    }

    public void UpdateAuthorizationState(string status, DateTimeOffset? expiresAt)
    {
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkRequiresNewAuthorization(string reason)
    {
        Status = PaymentStatus.RequiresNewAuthorization;
        LastFailureReason = reason;
        Touch();
    }

    public void RecordCapture(string captureId, string status, decimal? grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new OrderStateException($"Payment for order {OrderId} is not in an authorized state (current: {Status}).");
        }

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void RecordVoid(string status)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new OrderStateException($"Payment for order {OrderId} is not in an authorized state (current: {Status}).");
        }

        AuthorizationStatus = status;
        Status = PaymentStatus.Voided;
        Touch();
    }

    public decimal RefundableAmount() => (CapturedAmount ?? Amount) - TotalRefunded();

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public PaymentRefund RegisterRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new OrderStateException($"Payment for order {OrderId} is not captured (current: {Status}); nothing can be refunded.");
        }
        if (amount <= 0 || amount > RefundableAmount())
        {
            throw new OrderStateException($"Refund amount {amount:0.00} {Currency} exceeds the refundable balance {RefundableAmount():0.00} {Currency} for order {OrderId}.");
        }

        var refund = new PaymentRefund(Id, payPalRefundId, amount, Currency, status, idempotencyKey);
        _refunds.Add(refund);
        Status = RefundableAmount() <= 0 ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        if (status == "COMPLETED" || status == "PENDING")
        {
            CaptureStatus = Status == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }
        Touch();
        return refund;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
