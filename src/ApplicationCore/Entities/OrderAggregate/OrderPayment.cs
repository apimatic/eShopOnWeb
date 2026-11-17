using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment state for an order. The application only stores identifiers and
/// Finix-owned state here; card details are never persisted.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string Currency { get; private set; }

    public int AuthorizedAmountCents { get; private set; }
    public int CapturedAmountCents { get; private set; }
    public int FeeCents { get; private set; }
    public int NetCents { get; private set; }
    public int RefundedAmountCents { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public DateTimeOffset? FinixAuthorizationExpiresAt { get; private set; }
    public string? FinixCaptureTransferId { get; private set; }
    public string? FinixCaptureState { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorized(string authorizationId, string state, DateTimeOffset? expiresAt, int amountCents)
    {
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = state;
        FinixAuthorizationExpiresAt = expiresAt;
        AuthorizedAmountCents = amountCents;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkAuthorizationExpired(string message)
    {
        Status = PaymentStatus.AuthorizationExpired;
        FailureCode = "AUTHORIZATION_EXPIRED";
        FailureMessage = message;
        Touch();
    }

    public void MarkVoided(string? state = null)
    {
        if (state != null)
        {
            FinixAuthorizationState = state;
        }
        Status = PaymentStatus.Voided;
        Touch();
    }

    public void MarkCaptured(string transferId, string transferState, int capturedAmountCents, int feeCents, int netCents)
    {
        FinixCaptureTransferId = transferId;
        FinixCaptureState = transferState;
        CapturedAt = DateTimeOffset.UtcNow;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = netCents;
        Status = PaymentStatus.Captured;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkCaptureFailed(string? code, string message)
    {
        Status = PaymentStatus.CaptureFailed;
        FailureCode = code;
        FailureMessage = message;
        Touch();
    }

    public void MarkFailed(string? code, string message)
    {
        Status = PaymentStatus.Failed;
        FailureCode = code;
        FailureMessage = message;
        Touch();
    }

    public void ResetForRetry()
    {
        if (CapturedAmountCents > 0 || RefundedAmountCents > 0)
        {
            return;
        }

        Status = PaymentStatus.Pending;
        FinixAuthorizationId = null;
        FinixAuthorizationState = null;
        FinixAuthorizationExpiresAt = null;
        AuthorizedAmountCents = 0;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public PaymentRefund AddRefund(string reversalId, string reversalState, int amountCents, string idempotencyKey)
    {
        var refund = new PaymentRefund(Id, reversalId, reversalState, amountCents, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmountCents += amountCents;
        Status = RefundedAmountCents >= CapturedAmountCents
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    public void UpdateRefundState(string reversalId, string reversalState)
    {
        var refund = _refunds.FirstOrDefault(r => r.FinixReversalId == reversalId);
        refund?.SetState(reversalState);
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
