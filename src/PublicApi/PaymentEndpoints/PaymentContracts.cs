using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CreateOrderRequest
{
    public List<CreateOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class CreateOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    public CardRequest? Card { get; set; }
}

public sealed class CardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public sealed class CardBillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public sealed class RefundOrderRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Note { get; set; }
}

public sealed record CreateOrderResponse(int OrderId, OrderDto Order);
public sealed record PayOrderResponse(int OrderId, OrderDto Order);
public sealed record FulfilOrderResponse(int OrderId, OrderDto Order);
public sealed record CancelOrderResponse(int OrderId, OrderDto Order);
public sealed record RefundOrderResponse(string RefundId, int OrderId, OrderDto Order);
public sealed record MyOrdersResponse(IReadOnlyList<OrderDto> Orders);
public sealed record SavePaymentMethodResponse(int PaymentMethodId, PaymentMethodDto PaymentMethod);
public sealed record PaymentMethodsResponse(IReadOnlyList<PaymentMethodDto> PaymentMethods);

public sealed record OrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string FulfillmentStatus,
    DateTimeOffset? FulfilledAt,
    DateTimeOffset? CancelledAt,
    PaymentDto? Payment,
    IReadOnlyList<OrderItemDto> Items);

public sealed record OrderItemDto(
    int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

public sealed record PaymentDto(
    string State,
    string Currency,
    string? PayPalOrderId,
    string? PayPalOrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    IReadOnlyList<RefundDto> Refunds);

public sealed record RefundDto(
    string? RefundId,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset CreatedAt);

public sealed record PaymentMethodDto(
    int PaymentMethodId,
    string Brand,
    string LastDigits,
    string? Expiry,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReconciliationTransactionDto> PayPalTransactions,
    IReadOnlyList<LocalTransactionDto> EShopTransactions);

public sealed record ReconciliationTransactionDto(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? GrossAmount,
    decimal? FeeAmount,
    string? Currency,
    DateTimeOffset? InitiatedAt,
    int? OrderId,
    string MatchStatus);

public sealed record LocalTransactionDto(
    int OrderId,
    string Kind,
    string PayPalId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? OccurredAt,
    string MatchStatus);
