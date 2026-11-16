using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment state eShop owns for an order. It stores Finix resource ids and the
/// last state Finix reported; it never stores card numbers or security codes.
/// </summary>
public class OrderPayment : BaseEntity
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = PaymentStatuses.AwaitingPayment;
    public string? FinixIdentityId { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? TransferId { get; set; }
    public long OrderTotalCents { get; set; }
    public long AuthorizedAmountCents { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FeeCents { get; set; }
    public long NetCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public string? IdempotencyId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
