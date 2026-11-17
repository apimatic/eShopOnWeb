using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment
{
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, long totalCents, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        TotalCents = totalCents;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        PaymentAttemptKey = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public long TotalCents { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string PaymentAttemptKey { get; private set; } = string.Empty;

    public string? PaymentMethodId { get; private set; }
    public string? OneOffInstrumentId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? HoldExpiresAt { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? TransferState { get; private set; }
    public long CapturedCents { get; private set; }
    public long? FeeCents { get; private set; }
    public long AmountRefundedCents { get; private set; }

    public string? LastFailureCode { get; private set; }
    public string? LastFailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RotateAttemptKey()
    {
        PaymentAttemptKey = Guid.NewGuid().ToString("N");
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAuthorized(string authorizationId, string? authorizationStatus, DateTimeOffset? holdExpiresAt,
        string? paymentMethodId, string? oneOffInstrumentId)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        HoldExpiresAt = holdExpiresAt;
        PaymentMethodId = paymentMethodId;
        OneOffInstrumentId = oneOffInstrumentId;
        Status = PaymentStatus.Authorized;
        LastFailureCode = null;
        LastFailureMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPaymentFailed(string? code, string? message)
    {
        Status = PaymentStatus.AwaitingPayment;
        LastFailureCode = code;
        LastFailureMessage = message;
        AuthorizationId = null;
        AuthorizationStatus = null;
        HoldExpiresAt = null;
        RotateAttemptKey();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPaymentUnknown(string? code, string? message)
    {
        Status = PaymentStatus.AwaitingPayment;
        LastFailureCode = code ?? "UNKNOWN";
        LastFailureMessage = message;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCaptured(string transferId, string? transferState, long capturedCents, long? feeCents)
    {
        CaptureTransferId = transferId;
        TransferState = transferState;
        CapturedCents = capturedCents;
        FeeCents = feeCents;
        Status = PaymentStatus.Captured;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateTransfer(string? transferState, long? feeCents)
    {
        TransferState = transferState;
        if (feeCents.HasValue)
        {
            FeeCents = feeCents;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCanceled(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Canceled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordRefund(long amountCents)
    {
        AmountRefundedCents += amountCents;
        Status = AmountRefundedCents >= CapturedCents ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public long RefundableCents => Math.Max(0, CapturedCents - AmountRefundedCents);
}
