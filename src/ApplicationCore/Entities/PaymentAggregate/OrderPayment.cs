using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state for an order. Kept separate from <see cref="OrderAggregate.Order"/> so the
/// existing order model remains untouched.
/// </summary>
public class OrderPayment
{
    public const string AwaitingPayment = "AWAITING_PAYMENT";
    public const string Authorized = "AUTHORIZED";
    public const string Captured = "CAPTURED";
    public const string Canceled = "CANCELED";
    public const string PartiallyRefunded = "PARTIALLY_REFUNDED";
    public const string Refunded = "REFUNDED";
    public const string PaymentFailed = "PAYMENT_FAILED";

    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>
    /// A per-order random key. It keeps Finix idempotency keys unique across processes and runs
    /// (order ids restart when the in-memory store is recreated).
    /// </summary>
    public string ProviderKey { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";
    public long AmountCents { get; set; }
    public string Status { get; set; } = AwaitingPayment;

    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public bool IsVoid { get; set; }
    public string? VoidState { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureTransferId { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public long RefundedCents { get; set; }

    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
