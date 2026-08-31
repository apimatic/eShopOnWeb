using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<PlaceOrderItemRequest> Items { get; set; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class PlaceOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public sealed class CardRequest
{
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [Required, RegularExpression(@"^[0-9 -]{13,23}$")] public string Number { get; set; } = string.Empty;
    [Required, RegularExpression(@"^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; set; } = string.Empty;
    [Required, RegularExpression(@"^[0-9]{3,4}$")] public string SecurityCode { get; set; } = string.Empty;
    [Required] public BillingAddressRequest BillingAddress { get; set; } = new();
}

public sealed class BillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; set; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; set; }
    [Required, MaxLength(120)] public string City { get; set; } = string.Empty;
    [MaxLength(300)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(60)] public string PostalCode { get; set; } = string.Empty;
    [Required, RegularExpression(@"^[A-Za-z]{2}$")] public string CountryCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class RefundOrderRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999999999999.99")]
    public decimal? Amount { get; set; }
}

public sealed record CreateOrderResponse(int OrderId, string Status, decimal Total, string Currency);
public sealed record CreatePaymentMethodResponse(int PaymentMethodId, string Brand, string LastFour, string Expiry);
public sealed record RefundResponse(int RefundId, string PayPalRefundId, string Status, decimal Amount, string Currency);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string LastFour, string Expiry, DateTimeOffset CreatedAt);
public sealed record OrderItemResponse(int CatalogItemId, string ProductName, int Quantity, decimal UnitPrice);
public sealed record RefundDetailResponse(int RefundId, string PayPalRefundId, string Status, decimal Amount, string Currency, DateTimeOffset CreatedAt);
public sealed record PaymentResponse(
    string Status,
    string? PayPalOrderId,
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
    string Currency,
    string? CardBrand,
    string? CardLastFour,
    IReadOnlyList<RefundDetailResponse> Refunds);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<OrderItemResponse> Items,
    PaymentResponse Payment);

public sealed record ReconciliationEntryResponse(
    string MatchStatus,
    int? OrderId,
    string? LocalRecordType,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? PayPalStatus,
    string? EventCode,
    decimal Amount,
    string Currency,
    decimal? Fee,
    DateTimeOffset? OccurredAt);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? PayPalLastRefreshedAt,
    IReadOnlyList<ReconciliationEntryResponse> Entries);
