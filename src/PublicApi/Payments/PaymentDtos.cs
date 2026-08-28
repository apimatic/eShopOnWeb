using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    [Required]
    public AddressRequest ShipToAddress { get; set; } = new();
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public sealed class AddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public sealed class CreateOrderResponse
{
    public int OrderId { get; init; }
    public string PaymentStatus { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    [Required]
    public CardRequest Card { get; set; } = new();
}

public sealed class CardRequest
{
    [Required, RegularExpression("^[0-9 ]{13,23}$")]
    public string Number { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    public string Expiry { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9]{3,4}$")]
    public string SecurityCode { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public BillingAddressRequest BillingAddress { get; set; } = new();
}

public sealed class BillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; set; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; set; }
    [Required, MaxLength(120)] public string City { get; set; } = string.Empty;
    [MaxLength(300)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(60)] public string PostalCode { get; set; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; set; } = string.Empty;
}

public sealed class PaymentMethodResponse
{
    public int PaymentMethodId { get; init; }
    public string Brand { get; init; } = string.Empty;
    public string LastDigits { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string? CardholderName { get; init; }
}

public class OrderPaymentResponse
{
    public int OrderId { get; init; }
    public string PaymentStatus { get; init; } = string.Empty;
    public string FulfillmentStatus { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public string? CardBrand { get; init; }
    public string? CardLastDigits { get; init; }
    public IReadOnlyList<RefundSummaryResponse> Refunds { get; init; } = Array.Empty<RefundSummaryResponse>();
}

public sealed class MyOrderResponse : OrderPaymentResponse
{
    public DateTimeOffset OrderDate { get; init; }
    public IReadOnlyList<MyOrderItemResponse> Items { get; init; } = Array.Empty<MyOrderItemResponse>();
}

public sealed class MyOrderItemResponse
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}

public sealed class RefundOrderRequest
{
    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal? Amount { get; set; }
}

public sealed class RefundResponse
{
    public string RefundId { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal RemainingRefundableAmount { get; init; }
}

public sealed class RefundSummaryResponse
{
    public string? RefundId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class ReconciliationResponse
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationEntryResponse> Entries { get; init; } = Array.Empty<ReconciliationEntryResponse>();
}

public sealed class ReconciliationEntryResponse
{
    public string ReconciliationStatus { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public int? OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? TransactionType { get; init; }
    public string? TransactionStatus { get; init; }
    public DateTimeOffset? TransactionDate { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? Fee { get; init; }
}
