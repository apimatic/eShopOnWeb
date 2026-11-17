using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CreateOrderItemCommand(int CatalogItemId, int Quantity);

public sealed class CreateOrderCommand
{
    public string BuyerId { get; set; } = string.Empty;
    public List<CreateOrderItemCommand> Items { get; set; } = new();
    public ShippingAddressCommand? ShippingAddress { get; set; }
}

public sealed class ShippingAddressCommand
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed class CardCommand
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public ShippingAddressCommand? BillingAddress { get; set; }
}

public sealed class PayOrderCommand
{
    public string BuyerId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardCommand? Card { get; set; }
}

public sealed class RefundOrderCommand
{
    public int OrderId { get; set; }
    public int? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class SaveCardCommand
{
    public string BuyerId { get; set; } = string.Empty;
    public CardCommand Card { get; set; } = new();
}

public sealed class OrderPaymentView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentView? Payment { get; set; }
    public List<RefundView> Refunds { get; set; } = new();
    public List<OrderItemView> Items { get; set; } = new();
}

public sealed class OrderItemView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public sealed class PaymentView
{
    public string Status { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PaymentInstrumentId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? TransferId { get; set; }
    public string? CaptureState { get; set; }
    public int CapturedAmountCents { get; set; }
    public int FeeCents { get; set; }
    public int NetCents { get; set; }
    public int RefundedAmountCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public sealed class RefundView
{
    public int RefundId { get; set; }
    public string FinixRefundId { get; set; } = string.Empty;
    public string ParentTransferId { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SavedPaymentMethodView
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? CardType { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string MerchantId { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public int FinixTransferCount { get; set; }
    public int FinixAuthorizationCount { get; set; }
    public int EshopPaymentCount { get; set; }
    public List<ReconciliationTransferItem> Transfers { get; set; } = new();
    public List<ReconciliationAuthorizationItem> Authorizations { get; set; } = new();
    public List<ReconciliationEshopOnlyItem> EshopOnly { get; set; } = new();
}

public sealed class ReconciliationTransferItem
{
    public string FinixTransferId { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public int AmountCents { get; set; }
    public string? Currency { get; set; }
    public string? ParentTransferId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? MatchedLocalType { get; set; }
    public int? MatchedLocalId { get; set; }
}

public sealed class ReconciliationAuthorizationItem
{
    public string FinixAuthorizationId { get; set; } = string.Empty;
    public string? State { get; set; }
    public int AmountCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedLocalId { get; set; }
}

public sealed class ReconciliationEshopOnlyItem
{
    public string LocalType { get; set; } = string.Empty;
    public int LocalId { get; set; }
    public int OrderId { get; set; }
    public string FinixId { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public string? State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
