using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity, IAggregateRoot
{
    private PaymentRefund() { }

    public PaymentRefund(int orderPaymentId, int orderId, string buyerId, int amountCents, string idempotencyKey)
    {
        OrderPaymentId = orderPaymentId;
        OrderId = orderId;
        BuyerId = buyerId;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        State = "PENDING";
    }

    public int OrderPaymentId { get; private set; }
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? FinixTransferId { get; private set; }
    public string? State { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkSucceeded(string finixTransferId, string state)
    {
        FinixTransferId = finixTransferId;
        State = state;
        FailureCode = null;
        FailureMessage = null;
    }

    public void MarkFailed(string? code, string? message)
    {
        State = "FAILED";
        FailureCode = code;
        FailureMessage = message;
    }

    public void UpdateState(string state)
    {
        State = state;
    }
}
