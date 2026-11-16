using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, int amountCents, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        AmountCents = amountCents;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        AuthorizationIdempotencyKey = $"order-{orderId}-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public int AmountCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    public string? FinixIdentityId { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? AuthorizationIdempotencyKey { get; private set; }

    public string? FinixCaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public int? CapturedAmountCents { get; private set; }
    public int? FeeAmountCents { get; private set; }
    public int? NetAmountCents { get; private set; }
    public bool FeesPending { get; private set; }
    public int RefundedAmountCents { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetIdentity(string identityId)
    {
        FinixIdentityId = identityId;
    }

    public void MarkAuthorized(string authorizationId, string state, DateTimeOffset? expiresAt,
        string paymentInstrumentId, string idempotencyKey)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizationExpiresAt = expiresAt;
        FinixPaymentInstrumentId = paymentInstrumentId;
        AuthorizationIdempotencyKey = idempotencyKey;
        Status = PaymentStatus.Authorized;
        AuthorizedAt = DateTimeOffset.UtcNow;
        FailureCode = null;
        FailureMessage = null;
    }

    public void MarkAuthorizationFailed(string? code, string? message)
    {
        FailureCode = code;
        FailureMessage = message;
        Status = PaymentStatus.AwaitingPayment;
    }

    public void RotateAuthorizationIdempotencyKey()
    {
        AuthorizationIdempotencyKey = $"order-{OrderId}-{Guid.NewGuid():N}";
    }

    public void MarkCaptured(string transferId, string captureState, int capturedAmountCents,
        int? feeAmountCents, int? netAmountCents)
    {
        FinixCaptureTransferId = transferId;
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FeeAmountCents = feeAmountCents;
        NetAmountCents = netAmountCents;
        FeesPending = feeAmountCents is null;
        Status = PaymentStatus.Captured;
        CapturedAt = DateTimeOffset.UtcNow;
        FailureCode = null;
        FailureMessage = null;
    }

    public void UpdateFees(int feeAmountCents, int netAmountCents)
    {
        FeeAmountCents = feeAmountCents;
        NetAmountCents = netAmountCents;
        FeesPending = false;
    }

    public void UpdateCaptureState(string state)
    {
        CaptureState = state;
    }

    public void MarkCancelled(string? voidState)
    {
        AuthorizationState = voidState ?? AuthorizationState;
        Status = PaymentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
        RefundedAmountCents += refund.AmountCents;
        Status = CapturedAmountCents.HasValue && RefundedAmountCents >= CapturedAmountCents.Value
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}
