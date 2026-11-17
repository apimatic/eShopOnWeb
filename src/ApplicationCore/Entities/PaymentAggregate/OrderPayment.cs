using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment owned by Finix for a single eShop order. Only Finix identifiers and
/// non-sensitive card metadata are stored here; card numbers and security codes never are.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, string currency, long amountMinor)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AmountMinor = amountMinor;
        Status = PaymentStatus.AwaitingAuthorization;
        AuthorizationIdempotencyKey = NewIdempotencyKey(orderId);
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public long AmountMinor { get; private set; }
    public PaymentStatus Status { get; private set; }

    /// <summary>Idempotency key used for the Finix Authorization request.</summary>
    public string AuthorizationIdempotencyKey { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public string? VoidState { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }

    public string? FinixCaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public long? CapturedAmountMinor { get; private set; }
    public decimal? FinixFeeMinor { get; private set; }
    public decimal? NetAmountMinor { get; private set; }
    public long RefundedAmountMinor { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public static string NewIdempotencyKey(int orderId) =>
        $"order-{orderId}-authorization-{Guid.NewGuid():N}";

    public void BeginRetry(string idempotencyKey)
    {
        Status = PaymentStatus.AwaitingAuthorization;
        AuthorizationIdempotencyKey = idempotencyKey;
        FinixAuthorizationId = null;
        AuthorizationState = null;
        VoidState = null;
        FinixPaymentInstrumentId = null;
        SavedPaymentMethodId = null;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkAuthorized(string authorizationId, string authorizationState,
        string paymentInstrumentId, int? savedPaymentMethodId)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        FinixPaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkAuthorizationFailed(string? failureCode, string? failureMessage)
    {
        Status = PaymentStatus.Failed;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        Touch();
    }

    public void MarkAuthorizationPending(string authorizationId, string authorizationState,
        string paymentInstrumentId, int? savedPaymentMethodId)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        FinixPaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.AwaitingAuthorization;
        Touch();
    }

    public void MarkCaptured(string transferId, string captureState, long capturedAmountMinor,
        decimal? finixFeeMinor, decimal? netAmountMinor)
    {
        FinixCaptureTransferId = transferId;
        CaptureState = captureState;
        CapturedAmountMinor = capturedAmountMinor;
        FinixFeeMinor = finixFeeMinor;
        NetAmountMinor = netAmountMinor;
        if (string.Equals(captureState, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            Status = PaymentStatus.CapturePending;
        }
        else if (string.Equals(captureState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            Status = PaymentStatus.Captured;
        }

        CapturedAt = DateTimeOffset.UtcNow;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkCaptureFailed(string failureCode, string failureMessage)
    {
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        Touch();
    }

    public void MarkCanceled(string? authorizationState, string? voidState)
    {
        Status = PaymentStatus.Canceled;
        if (!string.IsNullOrWhiteSpace(authorizationState))
        {
            AuthorizationState = authorizationState;
        }
        if (!string.IsNullOrWhiteSpace(voidState))
        {
            VoidState = voidState;
        }
        Touch();
    }

    public void ApplyRefundTotals(long activeRefundedMinor)
    {
        RefundedAmountMinor = activeRefundedMinor;
        if (activeRefundedMinor <= 0)
        {
            Touch();
            return;
        }

        var capturedMinor = CapturedAmountMinor ?? AmountMinor;
        Status = activeRefundedMinor >= capturedMinor
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
