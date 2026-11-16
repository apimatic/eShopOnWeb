using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        PaymentReference = Guid.NewGuid().ToString("N");
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public string PaymentReference { get; private set; }
    public PaymentStatus Status { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public long AuthorizedAmountCents { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public string? FinixCaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public long CapturedAmountCents { get; private set; }
    public long FinixFeeCents { get; private set; }
    public long NetProceedsCents { get; private set; }
    public string? CaptureUnavailableReason { get; private set; }

    public long RefundedAmountCents { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkAuthorized(
        string authorizationId,
        string? authorizationState,
        long authorizedAmountCents,
        string? paymentInstrumentId,
        string? cardBrand,
        string? cardLast4)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NegativeOrZero(authorizedAmountCents, nameof(authorizedAmountCents));

        FinixAuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        AuthorizedAmountCents = authorizedAmountCents;
        FinixPaymentInstrumentId = paymentInstrumentId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        CaptureUnavailableReason = null;
        Touch();
    }

    public void MarkAuthorizationFailed(string? failureCode, string? failureMessage)
    {
        Status = PaymentStatus.PaymentFailed;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        Touch();
    }

    public void UpdateAuthorizationState(string? authorizationState)
    {
        AuthorizationState = authorizationState;
        Touch();
    }

    public void MarkCaptured(
        string captureTransferId,
        string? captureState,
        long capturedAmountCents,
        long finixFeeCents,
        long netProceedsCents)
    {
        Guard.Against.NullOrEmpty(captureTransferId, nameof(captureTransferId));

        FinixCaptureTransferId = captureTransferId;
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetProceedsCents = netProceedsCents;
        CaptureUnavailableReason = null;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkCaptureUnavailable(string reason)
    {
        Guard.Against.NullOrEmpty(reason, nameof(reason));
        CaptureUnavailableReason = reason;
        Status = PaymentStatus.CaptureUnavailable;
        Touch();
    }

    public void UpdateCaptureDetails(
        string? captureState,
        long capturedAmountCents,
        long finixFeeCents,
        long netProceedsCents)
    {
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetProceedsCents = netProceedsCents;
        Touch();
    }

    public void MarkVoided(string? voidState)
    {
        Status = PaymentStatus.Cancelled;
        AuthorizationState = voidState ?? "SUCCEEDED";
        Touch();
    }

    public void MarkRefunded(long amountCents)
    {
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        RefundedAmountCents += amountCents;
        Status = RefundedAmountCents >= CapturedAmountCents
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
