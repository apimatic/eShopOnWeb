using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(int orderId, string finixTransferId, int amountCents, string currency,
        string status, string idempotencyKey, DateTimeOffset createdAt, string? failureCode = null,
        string? failureMessage = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrWhiteSpace(finixTransferId, nameof(finixTransferId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        OrderId = orderId;
        FinixTransferId = finixTransferId;
        AmountCents = amountCents;
        Currency = currency;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
    }

    public int OrderId { get; private set; }
    public string FinixTransferId { get; private set; }
    public int AmountCents { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
}
