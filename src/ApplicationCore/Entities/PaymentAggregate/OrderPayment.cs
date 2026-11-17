using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state owned by Finix for an eShop order: the hold, the capture and the money figures.
/// </summary>
public class OrderPayment
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingPayment;
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int AttemptNo { get; set; }

    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? AuthorizationIdempotencyId { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FeeAmountCents { get; set; }
    public long NetAmountCents { get; set; }
    public bool FeeComplete { get; set; }

    public long RefundedAmountCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
