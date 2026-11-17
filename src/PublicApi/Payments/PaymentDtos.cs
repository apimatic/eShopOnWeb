using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Cvc { get; set; } = string.Empty;
    public string? Cvv { get; set; }
    public string Name { get; set; } = string.Empty;
    public AddressDto? Address { get; set; }
}

public class PayOrderRequest
{
    public string? PaymentMethodId { get; set; }
    public string? SavedPaymentMethodId { get; set; }
    public string? SavedCardId { get; set; }
    public CardDto? Card { get; set; }

    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public AddressDto? Address { get; set; }
}

public class SavePaymentMethodRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Cvc { get; set; } = string.Empty;
    public string? Cvv { get; set; }
    public string Name { get; set; } = string.Empty;
    public AddressDto? Address { get; set; }
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public long? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? IdempotencyId { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public class RefundDto
{
    public Guid RefundId { get; set; }
    public int OrderId { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? FinixState { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;

    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? TransferId { get; set; }
    public string? TransferState { get; set; }
    public decimal? CapturedAmount { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? FinixFee { get; set; }
    public long? FinixFeeCents { get; set; }
    public decimal? NetProceeds { get; set; }
    public long? NetProceedsCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long RefundedAmountCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

public class PaymentMethodDto
{
    public Guid PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class FinixTransactionDto
{
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long? AmountCents { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public long? FeeCents { get; set; }
    public decimal? Fee { get; set; }
    public long? NetCents { get; set; }
    public decimal? Net { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentId { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? MatchSource { get; set; }
}

public class ReconciliationOrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? TransferId { get; set; }
    public List<Guid> RefundIds { get; set; } = new();
    public List<string> MatchedFinixTransactionIds { get; set; } = new();
}

public class ReconciliationDto
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int FinixTransactionCount { get; set; }
    public int EshopOrderCount { get; set; }
    public List<FinixTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrderDto> EshopOrders { get; set; } = new();
    public List<FinixTransactionDto> UnmatchedFinix { get; set; } = new();
    public List<ReconciliationOrderDto> UnmatchedEshop { get; set; } = new();
}
