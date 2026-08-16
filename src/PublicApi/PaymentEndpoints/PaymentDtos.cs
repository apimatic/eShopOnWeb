using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Requests ----

/// <summary>
/// Base for requests that act on the caller's own data. The identity fields are populated by the
/// endpoint from the validated JWT and are ignored by the JSON serializer, so a client can never
/// spoof them through the request body.
/// </summary>
public abstract class AuthenticatedRequest
{
    [JsonIgnore]
    public string CallerBuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool CallerIsAdmin { get; set; }
}

public class CreateOrderRequest : AuthenticatedRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = "N/A";
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "N/A";
    public string Country { get; set; } = "N/A";
    public string ZipCode { get; set; } = "00000";
}

public class CardDto
{
    /// <summary>The full card number. Passed to PayPal and never stored or logged by this app.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as year-month, e.g. "2030-01".</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto BillingAddress { get; set; } = new();
}

public class BillingAddressDto
{
    public string CountryCode { get; set; } = "US";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}

public class PayOrderRequest : AuthenticatedRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>One-off card details, OR set <see cref="SavedCardId"/> to pay with a saved card.</summary>
    public CardDto? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with.</summary>
    public int? SavedCardId { get; set; }
}

public class CreatePaymentMethodRequest : AuthenticatedRequest
{
    public CardDto Card { get; set; } = new();
}

public class RefundOrderRequest : AuthenticatedRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>The amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---- Responses ----

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentDto
{
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public AuthorizationDto? Authorization { get; set; }
    public CaptureDto? Capture { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

public class AuthorizationDto
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class CaptureDto
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class MyOrdersResponse
{
    public List<OrderPaymentResponse> Orders { get; set; } = new();
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public bool AlreadyProcessed { get; set; }
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodListResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}
