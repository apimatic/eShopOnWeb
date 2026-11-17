using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Canceled = 5,
    Failed = 6
}

/// <summary>
/// The payment state that belongs to an order. An order starts in AwaitingPayment;
/// money is only moved by the explicit pay / fulfil / cancel / refund operations.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, long amountInCents, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        AmountInCents = amountInCents;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public long AmountInCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public bool AuthorizationVoided { get; private set; }
    public string AuthorizationIdempotencyKey { get; private set; } = string.Empty;

    public string? FinixCaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public long? CapturedAmountInCents { get; private set; }
    public long? FinixFeeInCents { get; private set; }
    public long? NetAmountInCents { get; private set; }
    public long RefundedAmountInCents { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void MarkAuthorized(string authorizationId, string state, DateTimeOffset? expiresAt)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationVoided = false;
        Status = PaymentStatus.Authorized;
        AuthorizedAt = DateTimeOffset.UtcNow;
        FailureCode = null;
        FailureMessage = null;
    }

    public void RefreshAuthorization(string? state, DateTimeOffset? expiresAt, bool isVoid)
    {
        AuthorizationState = state ?? AuthorizationState;
        AuthorizationExpiresAt = expiresAt ?? AuthorizationExpiresAt;
        AuthorizationVoided = isVoid || AuthorizationVoided;
    }

    public void MarkAuthorizationFailed(string? code, string? message)
    {
        Status = PaymentStatus.Failed;
        FailureCode = code;
        FailureMessage = message;
        // A new key is used if the shopper retries a failed authorization.
        AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public void MarkVoided(string? state)
    {
        AuthorizationState = state ?? AuthorizationState;
        AuthorizationVoided = true;
        Status = PaymentStatus.Canceled;
        CanceledAt = DateTimeOffset.UtcNow;
    }

    public void MarkCanceled()
    {
        Status = PaymentStatus.Canceled;
        CanceledAt = DateTimeOffset.UtcNow;
    }

    public void MarkCaptured(
        string transferId,
        string state,
        long capturedAmountInCents,
        long feeInCents)
    {
        FinixCaptureTransferId = transferId;
        CaptureState = state;
        CapturedAmountInCents = capturedAmountInCents;
        FinixFeeInCents = feeInCents;
        NetAmountInCents = capturedAmountInCents - feeInCents;
        CapturedAt ??= DateTimeOffset.UtcNow;
        Status = RefundedAmountInCents > 0 ? PaymentStatus.PartiallyRefunded : PaymentStatus.Captured;
        FailureCode = null;
        FailureMessage = null;
    }

    public void RefreshCapture(string state, long capturedAmountInCents, long feeInCents)
    {
        CaptureState = state;
        CapturedAmountInCents = capturedAmountInCents;
        FinixFeeInCents = feeInCents;
        NetAmountInCents = capturedAmountInCents - feeInCents;
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
        RefundedAmountInCents += refund.AmountInCents;
        Status = CapturedAmountInCents.HasValue && RefundedAmountInCents >= CapturedAmountInCents.Value
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    public void MarkFailed(string? code, string? message)
    {
        Status = PaymentStatus.Failed;
        FailureCode = code;
        FailureMessage = message;
    }
}
