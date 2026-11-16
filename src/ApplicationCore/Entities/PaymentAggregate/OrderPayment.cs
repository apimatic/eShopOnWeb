using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state owned by eShop for an order. The identifiers and status values
/// returned by Finix are persisted here so a later request can act on the hold,
/// the capture and any refunds without needing the original request payload.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    // Required by Entity Framework.
    private OrderPayment()
    {
    }

    public OrderPayment(int orderId, string buyerId, long amountInCents, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amountInCents, nameof(amountInCents));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        AmountInCents = amountInCents;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        PaymentAttempt = 1;
        PaymentToken = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }

    public string BuyerId { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>The order total in the smallest currency unit (cents for USD).</summary>
    public long AmountInCents { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Finix Payment Instrument used for the authorization, when known.</summary>
    public string? PaymentInstrumentId { get; private set; }

    public string? FinixAuthorizationId { get; private set; }

    public string? AuthorizationState { get; private set; }

    public DateTimeOffset? AuthorizedAt { get; private set; }

    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>Idempotency key for the current authorization attempt.</summary>
    public string? AuthorizationIdempotencyKey { get; private set; }

    public int PaymentAttempt { get; private set; }

    /// <summary>Unique token for this payment record, used to build globally unique idempotency keys.</summary>
    public string PaymentToken { get; private set; }

    public string? FinixTransferId { get; private set; }

    public string? TransferState { get; private set; }

    public DateTimeOffset? CapturedAt { get; private set; }

    public long? CapturedAmountInCents { get; private set; }

    /// <summary>The fees Finix reported for the capture transfer, in cents.</summary>
    public long? FinixFeeInCents { get; private set; }

    /// <summary>Captured amount minus Finix's fee, in cents.</summary>
    public long? NetAmountInCents { get; private set; }

    public string? CaptureIdempotencyKey { get; private set; }

    public long RefundedAmountInCents { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    public string? VoidState { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long RefundableAmountInCents =>
        Math.Max(0, (CapturedAmountInCents ?? 0) - RefundedAmountInCents);

    public string EnsureAuthorizationIdempotencyKey()
    {
        AuthorizationIdempotencyKey ??= $"eshop-{PaymentToken}-auth-{PaymentAttempt}";
        Touch();
        return AuthorizationIdempotencyKey;
    }

    public string EnsureCaptureIdempotencyKey()
    {
        CaptureIdempotencyKey ??= $"eshop-{PaymentToken}-capture";
        Touch();
        return CaptureIdempotencyKey;
    }

    public void MarkAuthorized(string authorizationId, string authorizationState,
        DateTimeOffset? expiresAt, string paymentInstrumentId)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        FinixAuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        AuthorizationExpiresAt = expiresAt;
        PaymentInstrumentId = paymentInstrumentId;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
        LastError = null;
        Touch();
    }

    /// <summary>
    /// Records a definitive decline. The next pay request is allowed to start a
    /// fresh Finix attempt with a new idempotency key.
    /// </summary>
    public void MarkAuthorizationFailed(string error)
    {
        Status = PaymentStatus.PaymentFailed;
        AuthorizationState = "FAILED";
        LastError = error;
        PaymentAttempt++;
        AuthorizationIdempotencyKey = null;
        Touch();
    }

    /// <summary>
    /// Records an ambiguous failure (for example a network timeout). The attempt
    /// key is deliberately retained so a retry asks Finix about the same attempt
    /// instead of authorizing the shopper a second time.
    /// </summary>
    public void MarkAuthorizationUnknown(string error)
    {
        Status = PaymentStatus.PaymentFailed;
        LastError = error;
        Touch();
    }

    public void MarkCaptured(string transferId, string transferState, long capturedAmountInCents,
        long feeInCents, long netAmountInCents)
    {
        Guard.Against.NullOrEmpty(transferId, nameof(transferId));

        FinixTransferId = transferId;
        TransferState = transferState;
        CapturedAmountInCents = capturedAmountInCents;
        FinixFeeInCents = feeInCents;
        NetAmountInCents = netAmountInCents;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Fulfilled;
        LastError = null;
        Touch();
    }

    public void MarkCaptureFailed(string operatorMessage)
    {
        LastError = operatorMessage;
        Touch();
    }

    /// <summary>
    /// Refreshes the Finix-reported capture figures without changing the payment
    /// lifecycle (used when a later fulfil call can now see the fee records).
    /// </summary>
    public void UpdateCaptureDetails(string transferState, long capturedAmountInCents,
        long feeInCents, long netAmountInCents)
    {
        TransferState = transferState;
        CapturedAmountInCents = capturedAmountInCents;
        FinixFeeInCents = feeInCents;
        NetAmountInCents = netAmountInCents;
        Touch();
    }

    public void MarkCanceled(string? voidState)
    {
        VoidState = voidState;
        CanceledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Canceled;
        LastError = null;
        Touch();
    }

    public void MarkRefunded(long refundedAmountInCents)
    {
        Guard.Against.Negative(refundedAmountInCents, nameof(refundedAmountInCents));

        RefundedAmountInCents = refundedAmountInCents;
        Status = CapturedAmountInCents.HasValue && refundedAmountInCents >= CapturedAmountInCents.Value
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
