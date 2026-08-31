using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)] public List<PlaceOrderItemRequest> Items { get; set; } = new();
    [Required] public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class PlaceOrderItemRequest
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; set; }
    [Range(1, 1000)] public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class CardRequest
{
    [Required, RegularExpression("^[0-9]{13,19}$")] public string Number { get; set; } = string.Empty;
    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; set; } = string.Empty;
    [Required, RegularExpression("^[0-9]{3,4}$")] public string SecurityCode { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [Required] public BillingAddressRequest BillingAddress { get; set; } = new();
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

public sealed class SavePaymentMethodRequest : CardRequest
{
}

public sealed class RefundOrderRequest
{
    [Required, StringLength(108, MinimumLength = 1)] public string IdempotencyKey { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.01", "9999999999999.99")] public decimal? Amount { get; set; }
}

public sealed record PlaceOrderResponse(int OrderId, decimal Total, string Currency, string PaymentStatus,
    string FulfilmentStatus);

public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string LastFour, string Expiry,
    DateTimeOffset CreatedAt);

public sealed record RefundResponse(string RefundId, string Status, decimal Amount, string Currency,
    string IdempotencyKey);

public sealed class OrderResponse
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string FulfilmentStatus { get; init; } = string.Empty;
    public IReadOnlyList<OrderItemResponse> Items { get; init; } = Array.Empty<OrderItemResponse>();
    public PaymentResponse? Payment { get; init; }
}

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed class PaymentResponse
{
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
    public IReadOnlyList<RefundResponse> Refunds { get; init; } = Array.Empty<RefundResponse>();
}

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To, DateTimeOffset PayPalDataThrough,
    IReadOnlyList<ReconciliationEntry> Entries);

public sealed record ReconciliationEntry(string MatchStatus, string TransactionType, string TransactionId,
    int? OrderId, decimal? EShopAmount, decimal? PayPalAmount, decimal? PayPalFee, string? Currency,
    string? PayPalStatus, DateTimeOffset? TransactionDate, string? PayPalReferenceId, string? InvoiceId);

public sealed class ApiProblemException : Exception
{
    public ApiProblemException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
