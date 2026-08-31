using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CardInput
{
    [Required] public string Number { get; set; } = string.Empty;
    [Required] public string Expiry { get; set; } = string.Empty;
    [Required] public string SecurityCode { get; set; } = string.Empty;
    [Required] public string Name { get; set; } = string.Empty;
    [Required] public BillingAddressInput BillingAddress { get; set; } = new();
}

public sealed class BillingAddressInput
{
    [Required] public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
}

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)] public List<PlaceOrderItem> Items { get; set; } = new();
    [Required] public ShippingAddressInput ShipToAddress { get; set; } = new();
}

public sealed class PlaceOrderItem
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
}

public sealed class ShippingAddressInput
{
    [Required] public string Street { get; set; } = string.Empty;
    [Required] public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    [Required] public string Country { get; set; } = string.Empty;
    [Required] public string ZipCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardInput? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    [Required] public CardInput Card { get; set; } = new();
}

public sealed class RefundOrderRequest
{
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal? Amount { get; set; }
    [MaxLength(255)] public string? Note { get; set; }
}

public sealed record PlaceOrderResponse(int OrderId, decimal Total, string Currency, string PaymentStatus);
public sealed record SavePaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry);
public sealed record RefundResponse(string RefundId, decimal Amount, string Currency, string Status);

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundDetailsResponse(string RefundId, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string? Currency,
    string PaymentStatus,
    string FulfilmentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    decimal RefundedAmount,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<RefundDetailsResponse> Refunds);

public sealed record ReconciliationEntry(
    string Source,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? EShopTransactionId,
    int? OrderId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? TransactionDate,
    string MatchStatus);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationEntry> Entries);
