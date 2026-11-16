using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationIdempotencyKey { get; private set; }
    public string? AuthorizationState { get; private set; }
    public string? AuthorizationVoidState { get; private set; }
    public bool AuthorizationVoided { get; private set; }
    public long AuthorizedAmountCents { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? TransferState { get; private set; }
    public long CapturedAmountCents { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal FeeAmountCents { get; private set; }
    public decimal NetAmountCents { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public string? PaymentInstrumentId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Payment()
    {
        Currency = string.Empty;
    }

    public Payment(int orderId, string currency)
    {
        OrderId = orderId;
        Currency = currency;
    }

    public void Authorize(string authorizationId, string? state, long amountCents, string paymentInstrumentId,
        string? cardBrand, string? last4)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizedAmountCents = amountCents;
        PaymentInstrumentId = paymentInstrumentId;
        CardBrand = cardBrand;
        Last4 = last4;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void BeginAuthorization(string idempotencyKey)
    {
        AuthorizationIdempotencyKey = idempotencyKey;
        Touch();
    }

    public void RecordAuthorizationFailure(string? code, string? message, string? state)
    {
        AuthorizationState = state;
        FailureCode = code;
        FailureMessage = message;
        Touch();
    }

    public void UpdateAuthorizationState(string? state, string? voidState, bool isVoid)
    {
        AuthorizationState = state;
        AuthorizationVoidState = voidState;
        AuthorizationVoided = isVoid;
        Touch();
    }

    public void RecordFailure(string? code, string? message)
    {
        FailureCode = code;
        FailureMessage = message;
        Touch();
    }

    public void Capture(string transferId, string? transferState, long capturedAmountCents, decimal feeAmountCents)
    {
        FinixTransferId = transferId;
        TransferState = transferState;
        CapturedAmountCents = capturedAmountCents;
        FeeAmountCents = feeAmountCents;
        NetAmountCents = capturedAmountCents - feeAmountCents;
        CapturedAt = DateTimeOffset.UtcNow;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void Void(string? state, string? voidState)
    {
        AuthorizationState = state;
        AuthorizationVoidState = voidState;
        AuthorizationVoided = true;
        Touch();
    }

    public void UpdateTransferState(string? state)
    {
        TransferState = state;
        Touch();
    }

    public void RecordRefund(long amountCents)
    {
        RefundedAmountCents += amountCents;
        Touch();
    }

    public long RefundableAmountCents => Math.Max(0, CapturedAmountCents - RefundedAmountCents);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
