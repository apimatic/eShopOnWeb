using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class PaymentAddress
{
    public string? Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

public sealed class PaymentCardDetails
{
    public string Number { get; init; } = string.Empty;
    public int ExpirationMonth { get; init; }
    public int ExpirationYear { get; init; }
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public PaymentAddress? Address { get; init; }
}

public sealed class SavedCardInfo
{
    public int PaymentMethodId { get; init; }
    public string FinixPaymentInstrumentId { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string? LastFour { get; init; }
    public int ExpirationMonth { get; init; }
    public int ExpirationYear { get; init; }
    public string? CardType { get; init; }
    public string? Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class RefundInfo
{
    public string RefundId { get; init; } = string.Empty;
    public long AmountInCents { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? FinixState { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public string? LastError { get; init; }
}

public sealed class OrderPaymentInfo
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long AmountInCents { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? PaymentInstrumentId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationState { get; init; }
    public DateTimeOffset? AuthorizedAt { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }
    public string? CaptureTransferId { get; init; }
    public string? TransferState { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public long? CapturedAmountInCents { get; init; }
    public long? FinixFeeInCents { get; init; }
    public long? NetAmountInCents { get; init; }
    public long RefundedAmountInCents { get; init; }
    public long RefundableAmountInCents { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public string? VoidState { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<RefundInfo> Refunds { get; init; } = Array.Empty<RefundInfo>();
}

public sealed class OrderItemInfo
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public long UnitPriceInCents { get; init; }
    public int Quantity { get; init; }
    public long LineTotalInCents { get; init; }
}

public sealed class OrderInfo
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public DateTimeOffset OrderDate { get; init; }
    public long TotalInCents { get; init; }
    public string Currency { get; init; } = string.Empty;
    public PaymentAddress ShipToAddress { get; init; } = new();
    public IReadOnlyList<OrderItemInfo> Items { get; init; } = Array.Empty<OrderItemInfo>();
    public OrderPaymentInfo Payment { get; init; } = new();
}

public sealed class FinixTransactionInfo
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? State { get; init; }
    public long AmountInCents { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? ParentTransferId { get; init; }
    public int? MatchedOrderId { get; init; }
    public string MatchStatus { get; init; } = string.Empty;
}

public sealed class EshopPaymentInfo
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? AuthorizationId { get; init; }
    public string? CaptureTransferId { get; init; }
    public long? CapturedAmountInCents { get; init; }
    public long RefundedAmountInCents { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string MatchStatus { get; init; } = string.Empty;
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<FinixTransactionInfo> FinixTransactions { get; init; } = Array.Empty<FinixTransactionInfo>();
    public IReadOnlyList<EshopPaymentInfo> EshopPayments { get; init; } = Array.Empty<EshopPaymentInfo>();
    public IReadOnlyList<FinixTransactionInfo> MissingInEshop { get; init; } = Array.Empty<FinixTransactionInfo>();
    public IReadOnlyList<EshopPaymentInfo> MissingInFinix { get; init; } = Array.Empty<EshopPaymentInfo>();
}

public enum PaymentErrorKind
{
    Validation = 0,
    NotFound = 1,
    Conflict = 2,
    PaymentDeclined = 3,
    GatewayUnavailable = 4,
    HoldNotCapturable = 5
}

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(PaymentErrorKind kind, string message,
        string? operatorAction = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        OperatorAction = operatorAction;
    }

    public PaymentErrorKind Kind { get; }

    /// <summary>
    /// A short, actionable instruction intended for an operator, for example
    /// when a card hold can no longer be captured.
    /// </summary>
    public string? OperatorAction { get; }
}
