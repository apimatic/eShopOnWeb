using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The Finix-owned state for one eShop order. The order id is also the primary key so the
/// database itself refuses a second payment attempt for the same order.
/// </summary>
public class OrderPayment
{
    public int OrderId { get; set; }
    public string ShopperId { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Authorizing;
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyId { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public string? CaptureTransferId { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public long RefundedCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
