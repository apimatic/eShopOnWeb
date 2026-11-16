using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The Finix-owned state for an order payment: the authorization (hold), the capture
/// (transfer), and the amounts Finix reported for them.
/// </summary>
public class OrderPayment
{
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, string currency, long amountCents)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AmountCents = amountCents;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public long AmountCents { get; private set; }

    public string? FinixIdentityId { get; private set; }
    public string? PaymentInstrumentId { get; private set; }
    public string? PaymentIdempotencyKey { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public long? AuthorizedAmountCents { get; private set; }
    public bool? IsVoid { get; private set; }
    public string? VoidState { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FeeCents { get; private set; }
    public long? NetCents { get; private set; }
    public string? CaptureState { get; private set; }

    public string Status { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetPendingAttempt(string finixIdentityId, string paymentInstrumentId, string idempotencyKey)
    {
        FinixIdentityId = finixIdentityId;
        PaymentInstrumentId = paymentInstrumentId;
        PaymentIdempotencyKey = idempotencyKey;
        Status = PaymentStatus.AwaitingPayment;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void SetAuthorized(string finixIdentityId, string paymentInstrumentId, string authorizationId,
        string authorizationState, long authorizedAmountCents, bool? isVoid, string? voidState,
        DateTimeOffset? expiresAt, string idempotencyKey)
    {
        FinixIdentityId = finixIdentityId;
        PaymentInstrumentId = paymentInstrumentId;
        AuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        AuthorizedAmountCents = authorizedAmountCents;
        IsVoid = isVoid;
        VoidState = voidState;
        AuthorizationExpiresAt = expiresAt;
        PaymentIdempotencyKey = idempotencyKey;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void SetAuthorizationFailed(string? authorizationId, string? authorizationState,
        string? failureCode, string? failureMessage)
    {
        AuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        Status = PaymentStatus.Failed;
        Touch();
    }

    public void RefreshAuthorization(string? state, bool? isVoid, string? voidState,
        long? authorizedAmountCents, DateTimeOffset? expiresAt, string? failureCode, string? failureMessage)
    {
        if (state != null) AuthorizationState = state;
        IsVoid = isVoid;
        if (voidState != null) VoidState = voidState;
        if (authorizedAmountCents.HasValue) AuthorizedAmountCents = authorizedAmountCents;
        if (expiresAt.HasValue) AuthorizationExpiresAt = expiresAt;
        if (failureCode != null) FailureCode = failureCode;
        if (failureMessage != null) FailureMessage = failureMessage;
        Touch();
    }

    public void SetCaptured(string captureTransferId, long capturedAmountCents, long? feeCents,
        long? netCents, string? captureState)
    {
        CaptureTransferId = captureTransferId;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = netCents;
        CaptureState = captureState;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void SetVoided(bool? isVoid, string? voidState)
    {
        IsVoid = isVoid;
        VoidState = voidState;
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void SetStatus(string status)
    {
        Status = status;
        Touch();
    }

    public void SetUnknown(string message)
    {
        Status = PaymentStatus.Unknown;
        FailureMessage = message;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
