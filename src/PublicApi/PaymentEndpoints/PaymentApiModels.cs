using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderItemRequest
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
    public string PostalCode { get; set; } = string.Empty;
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
    public BillingAddressRequest? BillingAddress { get; set; }
}

public sealed class BillingAddressRequest
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
    public decimal? Amount { get; set; }
}

public sealed record OrderCreatedResponse(int OrderId, string Status, decimal Total, string Currency);
public sealed record RefundCreatedResponse(string RefundId, string Status, decimal Amount,
    decimal RefundedAmount, decimal RefundableAmount, string Currency);
public sealed record PaymentMethodCreatedResponse(int PaymentMethodId, string Brand,
    string LastDigits, string Expiry);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand,
    string LastDigits, string Expiry, DateTimeOffset CreatedAt);

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record AuthorizationResponse(string? PayPalOrderId, string? PayPalOrderStatus,
    string? PayPalAuthorizationId, string? PayPalAuthorizationStatus, decimal? Amount,
    DateTimeOffset? AuthorizedAt, DateTimeOffset? ExpiresAt);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount, DateTimeOffset CreatedAt);
public sealed record PaymentResponse(string Currency, AuthorizationResponse? Authorization,
    string? PayPalCaptureId, string? PayPalCaptureStatus, decimal? CapturedAmount,
    decimal? PayPalFee, decimal? NetAmount, decimal RefundedAmount, decimal RefundableAmount,
    IReadOnlyCollection<RefundResponse> Refunds);
public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, string Status,
    decimal Total, IReadOnlyCollection<OrderItemResponse> Items, PaymentResponse? Payment);

public sealed record ReconciliationEntry(
    string MatchStatus,
    int? OrderId,
    string? OrderStatus,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? PayPalEventCode,
    string? PayPalStatus,
    DateTimeOffset? PayPalInitiatedAt,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    string? Currency,
    string? ExternalReference);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    int PayPalTransactionCount, int EShopOrderCount, IReadOnlyCollection<ReconciliationEntry> Entries);
