using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingPayment;

    public string AuthorizationIdempotencyId { get; set; } = Guid.NewGuid().ToString();
    public string? FinixAuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? FinixTransferId { get; set; }
    public string? TransferState { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public long CapturedAmountCents { get; set; }
    public decimal FinixFeeCents { get; set; }
    public decimal NetProceedsCents { get; set; }

    public string? FinixPaymentInstrumentId { get; set; }
    public string? SavedCardPublicId { get; set; }

    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
