using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

// Tracks the PayPal-owned state (order/authorization/capture/refund ids and statuses) for a single
// Order's payment. Always accessed through its owning Order, never directly via a repository.
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        Amount = amount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int? PaymentMethodId { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds.Where(r => r.Status == RefundStatus.Completed).Sum(r => r.Amount);

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt, int? paymentMethodId)
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

    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void RecordAuthorizationExpired()
    {
        Status = PaymentStatus.AuthorizationExpired;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? feeAmount, decimal? netAmount, DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = PaymentStatus.Captured;
    }

    public void RecordVoid()
    {
        Status = PaymentStatus.Voided;
    }

    public Refund AddRefund(string payPalRefundId, decimal amount, RefundStatus status, string idempotencyKey, DateTimeOffset createdAt)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Payment for order {OrderId} cannot be refunded from status {Status}.");
        }

        var maxRefundable = (CapturedAmount ?? 0m) - RefundedAmount;
        if (status == RefundStatus.Completed && amount > maxRefundable)
        {
            throw new InvalidOperationException($"Refund amount {amount} exceeds the refundable balance {maxRefundable} for order {OrderId}.");
        }

        var refund = new Refund(Id, payPalRefundId, amount, status, idempotencyKey, createdAt);
        _refunds.Add(refund);

        if (status == RefundStatus.Completed)
        {
            Status = RefundedAmount >= (CapturedAmount ?? 0m) ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        }

        return refund;
    }
}
