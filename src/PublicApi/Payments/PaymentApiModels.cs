using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; init; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; init; } = new();
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    [Required] public string Street { get; init; } = string.Empty;
    [Required] public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    [Required] public string Country { get; init; } = string.Empty;
    [Required] public string ZipCode { get; init; } = string.Empty;
}

public sealed class CardRequest
{
    [Required] public string Name { get; init; } = string.Empty;
    [Required, CreditCard] public string Number { get; init; } = string.Empty;
    [Required, RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; init; } = string.Empty;
    [Required, RegularExpression(@"^\d{3,4}$")] public string SecurityCode { get; init; } = string.Empty;
    [Required] public CardBillingAddressRequest BillingAddress { get; init; } = new();
}

public sealed class CardBillingAddressRequest
{
    [Required] public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    [Required] public string City { get; init; } = string.Empty;
    [Required] public string State { get; init; } = string.Empty;
    [Required] public string PostalCode { get; init; } = string.Empty;
    [Required, RegularExpression(@"^[A-Za-z]{2}$")] public string CountryCode { get; init; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed class RefundOrderRequest
{
    public decimal? Amount { get; init; }
    [Required, MaxLength(256)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId, string PaymentStatus, decimal Total, string Currency);

public sealed record PayOrderResponse(int OrderId, PaymentStateResponse Payment);

public sealed record RefundOrderResponse(string RefundId, int OrderId, string Status, decimal Amount,
    decimal TotalRefunded, decimal RemainingRefundable);

public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry);

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record RefundStateResponse(string RefundId, string Status, decimal Amount, DateTimeOffset CreatedAt);

public sealed record PaymentStateResponse(
    string Status,
    string Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    decimal RefundedAmount,
    IReadOnlyList<RefundStateResponse> Refunds);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string FulfilmentStatus,
    IReadOnlyList<OrderItemResponse> Items,
    PaymentStateResponse Payment);

public sealed record ReconciliationEntryResponse(
    string MatchStatus,
    string Source,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? TransactionStatus,
    DateTimeOffset? TransactionDate,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? LocalPaymentType);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? PayPalLastRefreshedAt,
    IReadOnlyList<ReconciliationEntryResponse> Entries);
