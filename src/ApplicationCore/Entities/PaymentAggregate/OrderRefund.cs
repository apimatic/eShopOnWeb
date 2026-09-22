using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against an order's captured payment. <see cref="Id"/> is the <c>refundId</c>
/// returned to the caller. The caller-supplied <see cref="IdempotencyKey"/> forms a unique
/// alternate key with <see cref="OrderId"/>, so repeating a refund request under the same key is
/// rejected by the store (no double refund), while two distinct keys are two legitimate partial refunds.
/// </summary>
public class OrderRefund : IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(int orderId, string idempotencyKey, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        Id = Guid.NewGuid();
        OrderId = orderId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkCompleted(string? payPalRefundId, string? status)
    {
        PayPalRefundId = payPalRefundId;
        if (!string.IsNullOrEmpty(status)) Status = status!;
    }
}
