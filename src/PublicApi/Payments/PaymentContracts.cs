using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public sealed class OrderLineRequest
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
}

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)] public List<OrderLineRequest> Items { get; set; } = new();
    [Required] public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class CardRequest
{
    [Required, RegularExpression("^[0-9 ]{13,23}$")] public string Number { get; set; } = string.Empty;
    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; set; } = string.Empty;
    [Required, RegularExpression("^[0-9]{3,4}$")] public string SecurityCode { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [Required] public CardBillingAddressRequest BillingAddress { get; set; } = new();

    public override string ToString() => "[REDACTED CARD]";
}

public sealed class CardBillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; set; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; set; }
    [Required, MaxLength(120)] public string City { get; set; } = string.Empty;
    [MaxLength(300)] public string? State { get; set; }
    [Required, MaxLength(60)] public string PostalCode { get; set; } = string.Empty;
    [Required, RegularExpression("^[A-Z]{2}$")] public string CountryCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    [Required] public CardRequest Card { get; set; } = new();
}

public sealed class RefundOrderRequest
{
    [Required, MinLength(1), MaxLength(108)] public string IdempotencyKey { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.01", "9999999999999")] public decimal? Amount { get; set; }
}

public sealed record CreateOrderResponse(int OrderId, string Status, decimal Total, string Currency);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry,
    DateTimeOffset CreatedAt);
public sealed record PaymentStateResponse(string? PayPalOrderId, string? PayPalOrderStatus,
    string? AuthorizationId, string? AuthorizationStatus, DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId, string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee, decimal? NetAmount,
    decimal RefundedAmount, string Currency, string? CardBrand, string? CardLast4,
    IReadOnlyList<RefundStateResponse> Refunds);
public sealed record RefundStateResponse(string RefundId, string Status, decimal Amount,
    string IdempotencyKey, DateTimeOffset CreatedAt);
public sealed record OrderLineResponse(int CatalogItemId, string ProductName, int Quantity, decimal UnitPrice);
public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    IReadOnlyList<OrderLineResponse> Items, PaymentStateResponse Payment);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount, decimal RefundedAmount,
    decimal RefundableAmount);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationTransactionResponse> PayPalTransactions,
    IReadOnlyList<ReconciliationOrderResponse> EShopOrders,
    IReadOnlyList<string> UnmatchedPayPalTransactionIds,
    IReadOnlyList<int> OrdersWithNoPayPalTransaction);
public sealed record ReconciliationTransactionResponse(string TransactionId, string? ReferenceId,
    string? EventCode, DateTimeOffset? InitiatedAt, decimal? Amount, decimal? Fee, string? Currency,
    string? Status, string? InvoiceId, int? OrderId);
public sealed record ReconciliationOrderResponse(int OrderId, string InvoiceId, string Status, decimal Amount,
    string Currency, string? PayPalOrderId, string? AuthorizationId, string? CaptureId,
    IReadOnlyList<string> RefundIds, bool HasMatchingPayPalTransaction);
