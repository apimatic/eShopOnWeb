using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Finix-owned payment state for one eShop order. Keyed by the order id so there is at most
/// one payment per order; that primary key is the durable "pay" claim.
/// </summary>
public class OrderPayment
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";

    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingPayment;

    /// <summary>Order total, in the smallest currency unit (cents).</summary>
    public long AmountCents { get; set; }

    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public long? AuthorizedAmountCents { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }

    public long RefundedAmountCents { get; set; }

    /// <summary>The provider idempotency id used for the authorization hold.</summary>
    public string? IdempotencyId { get; set; }

    /// <summary>Application id of the saved card used, when one was used.</summary>
    public string? SavedCardId { get; set; }

    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? LastActionError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
