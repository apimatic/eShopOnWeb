using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<OrderRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, long amountCents, string currency)
    {
        OrderId = orderId;
        AmountCents = amountCents;
        Currency = currency;
        Status = OrderPaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public long AmountCents { get; private set; }
    public string Currency { get; private set; }
    public OrderPaymentStatus Status { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public bool AuthorizationIsVoid { get; private set; }
    public string? AuthorizationVoidState { get; private set; }
    public int AuthorizationAttempts { get; private set; }

    public string? FinixCaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FeeCents { get; private set; }
    public long? NetCents { get; private set; }
    public long RefundedAmountCents { get; private set; }

    public string? FinixPaymentInstrumentId { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public long RemainingRefundableCents =>
        Math.Max(0, (CapturedAmountCents ?? 0) - RefundedAmountCents);

    public void RecordAuthorizationAttempt()
    {
        AuthorizationAttempts++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorized(string authorizationId,
        string state,
        DateTimeOffset? expiresAt,
        bool isVoid,
        string? voidState,
        string? paymentInstrumentId,
        int? paymentMethodId,
        string? cardBrand,
        string? cardLast4)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationIsVoid = isVoid;
        AuthorizationVoidState = voidState;
        FinixPaymentInstrumentId = paymentInstrumentId;
        PaymentMethodId = paymentMethodId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        FailureCode = null;
        FailureMessage = null;
        Status = OrderPaymentStatus.Authorized;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorizationFailed(string? authorizationId,
        string? state,
        string? failureCode,
        string? failureMessage,
        string? paymentInstrumentId,
        string? cardBrand,
        string? cardLast4)
    {
        FinixAuthorizationId = authorizationId ?? FinixAuthorizationId;
        AuthorizationState = state;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        FinixPaymentInstrumentId = paymentInstrumentId ?? FinixPaymentInstrumentId;
        CardBrand = cardBrand ?? CardBrand;
        CardLast4 = cardLast4 ?? CardLast4;
        Status = OrderPaymentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorizationSnapshot(string state,
        DateTimeOffset? expiresAt,
        bool isVoid,
        string? voidState)
    {
        AuthorizationState = state;
        AuthorizationExpiresAt = expiresAt ?? AuthorizationExpiresAt;
        AuthorizationIsVoid = isVoid;
        AuthorizationVoidState = voidState;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCaptured(string captureTransferId,
        string? captureState,
        long capturedAmountCents,
        long feeCents,
        long netCents)
    {
        FinixCaptureTransferId = captureTransferId;
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = netCents;
        Status = RefundedAmountCents > 0
            ? (RefundedAmountCents >= capturedAmountCents ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded)
            : OrderPaymentStatus.Captured;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCancelled(string? voidState)
    {
        AuthorizationVoidState = voidState ?? AuthorizationVoidState;
        AuthorizationIsVoid = true;
        Status = OrderPaymentStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCancelledWithoutAuthorization()
    {
        Status = OrderPaymentStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public OrderRefund AddRefund(long amountCents,
        string idempotencyKey,
        string? finixTransferId,
        string status,
        string? failureCode,
        string? failureMessage)
    {
        var refund = new OrderRefund(Id, amountCents, idempotencyKey, finixTransferId, status, failureCode, failureMessage);
        _refunds.Add(refund);

        if (refund.CountsTowardsRefundedTotal)
        {
            RefundedAmountCents += amountCents;
            if (CapturedAmountCents.HasValue && CapturedAmountCents.Value > 0)
            {
                Status = RefundedAmountCents >= CapturedAmountCents.Value
                    ? OrderPaymentStatus.Refunded
                    : OrderPaymentStatus.PartiallyRefunded;
            }
        }
        else
        {
            FailureCode = failureCode;
            FailureMessage = failureMessage;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
        return refund;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
}
