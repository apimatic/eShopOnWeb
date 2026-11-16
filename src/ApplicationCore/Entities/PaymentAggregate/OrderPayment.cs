using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, long amountInCents, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        AmountInCents = amountInCents;
        Currency = currency;
        PaymentKey = Guid.NewGuid().ToString("N");
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string PaymentKey { get; private set; }
    public long AmountInCents { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public int AuthorizationAttempt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? FinixTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public long? CapturedAmountInCents { get; private set; }
    public long? FinixFeeInCents { get; private set; }
    public long? NetAmountInCents { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public long RefundedAmountInCents { get; private set; }

    public string? VoidState { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void MarkAuthorized(string authorizationId, string? state, DateTimeOffset? expiresAt)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizedAt = DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkAuthorizationFailed(string? code, string? message)
    {
        AuthorizationAttempt++;
        Status = PaymentStatus.AuthorizationFailed;
        FailureCode = code;
        FailureMessage = message;
        Touch();
    }

    public void MarkAuthorizationExpired(string? message)
    {
        Status = PaymentStatus.AuthorizationExpired;
        FailureMessage = message;
        Touch();
    }

    public void MarkCaptured(string transferId, string? state, long capturedAmountInCents,
        long feeInCents, long netAmountInCents)
    {
        FinixTransferId = transferId;
        CaptureState = state;
        CapturedAmountInCents = capturedAmountInCents;
        FinixFeeInCents = feeInCents;
        NetAmountInCents = netAmountInCents;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = RefundedAmountInCents <= 0
            ? PaymentStatus.Captured
            : RefundedAmountInCents >= capturedAmountInCents
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkCanceled(string? voidState)
    {
        VoidState = voidState;
        CanceledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Canceled;
        Touch();
    }

    public void SetCaptureState(string? state)
    {
        CaptureState = state;
        Touch();
    }

    public void SetFailure(string? code, string? message)
    {
        FailureCode = code;
        FailureMessage = message;
        Touch();
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
    }

    public void ApplyRefund(long amountInCents)
    {
        RefundedAmountInCents += amountInCents;
        if (CapturedAmountInCents.HasValue)
        {
            Status = RefundedAmountInCents >= CapturedAmountInCents.Value
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        }
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
