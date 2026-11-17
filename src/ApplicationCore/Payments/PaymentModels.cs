using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class ShippingAddressInput
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CardInput
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public ShippingAddressInput? BillingAddress { get; set; }
}

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderCommand
{
    public List<PlaceOrderItem> Items { get; set; } = new();
    public ShippingAddressInput? ShippingAddress { get; set; }
}

public class PayOrderCommand
{
    public int? PaymentMethodId { get; set; }
    public CardInput? Card { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderCommand
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class SaveCardCommand
{
    public CardInput? Card { get; set; }
}

public record PlaceOrderResult(int OrderId, decimal Total, string Currency, string PaymentStatus);

public record PaymentResult(
    int OrderId,
    string PaymentStatus,
    string? AuthorizationId,
    string? AuthorizationState,
    string? PaymentInstrumentId,
    decimal Amount,
    string Currency,
    bool IdempotentReplay);

public record FulfilmentResult(
    int OrderId,
    string PaymentStatus,
    string? TransferId,
    string? TransferState,
    decimal CapturedAmount,
    decimal FinixFee,
    decimal NetProceeds,
    string Currency,
    bool IdempotentReplay);

public record CancelResult(
    int OrderId,
    string PaymentStatus,
    string? AuthorizationId,
    string? AuthorizationState,
    string? Message,
    bool IdempotentReplay);

public record RefundResult(
    int RefundId,
    int OrderId,
    string? FinixRefundTransferId,
    string? FinixRefundState,
    decimal RefundedAmount,
    decimal TotalRefunded,
    decimal RemainingRefundable,
    string PaymentStatus,
    string Currency,
    bool IdempotentReplay);

public record SavedCardResult(
    int PaymentMethodId,
    string Brand,
    string Last4,
    int ExpirationMonth,
    int ExpirationYear,
    string? CardholderName,
    string? FinixPaymentInstrumentId);

public record OrderItemSummary(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderRefundSummary(int RefundId, decimal Amount, string? FinixRefundTransferId, string? FinixRefundState, DateTimeOffset CreatedAt);

public record OrderSummary(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string? AuthorizationId,
    string? AuthorizationState,
    string? TransferId,
    string? TransferState,
    decimal? CapturedAmount,
    decimal? FinixFee,
    decimal? NetProceeds,
    decimal RefundedAmount,
    decimal RefundableAmount,
    IReadOnlyList<OrderItemSummary> Items,
    IReadOnlyList<OrderRefundSummary> Refunds);

public record ReconciliationFinixTransaction(
    string Kind,
    string Id,
    string State,
    long AmountCents,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    int? OrderId,
    string? ParentTransferId,
    bool Matched);

public record ReconciliationOrder(
    int OrderId,
    string BuyerId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string? AuthorizationId,
    string? TransferId,
    IReadOnlyList<string> RefundTransferIds,
    bool Matched);

public record ReconciliationResult(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationFinixTransaction> FinixTransactions,
    IReadOnlyList<ReconciliationOrder> EshopOrders,
    IReadOnlyList<ReconciliationFinixTransaction> UnmatchedFinixTransactions,
    IReadOnlyList<ReconciliationOrder> UnmatchedEshopOrders,
    int FinixTransactionCount,
    int EshopOrderCount);
