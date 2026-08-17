using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// --- Shared card / address input (never persisted or logged in full) --------

public class CardRequest
{
    /// <summary>Full card number, e.g. the sandbox Visa 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }

    public PayPalCardDetails ToCardDetails() => new(
        Number, Expiry, SecurityCode, Name,
        BillingAddress?.ToPayPalAddress());
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public PayPalBillingAddress ToPayPalAddress() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}

// --- POST /api/orders -------------------------------------------------------

public class CreateOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }

    [JsonIgnore] public CallerContext Caller { get; set; } = new(string.Empty, false);
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

// --- POST /api/orders/{orderId}/pay ----------------------------------------

public class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public CallerContext Caller { get; set; } = new(string.Empty, false);
}

// --- POST /api/orders/{orderId}/refunds ------------------------------------

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? NoteToPayer { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public CallerContext Caller { get; set; } = new(string.Empty, false);
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public object? Payment { get; set; }
}

// --- Operator/lookup requests that only need the caller + route id ----------

public class OrderActionRequest
{
    public int OrderId { get; set; }
    public CallerContext Caller { get; set; } = new(string.Empty, false);
}

public class CallerOnlyRequest
{
    public CallerContext Caller { get; set; } = new(string.Empty, false);
}

// --- POST /api/payment-methods ---------------------------------------------

public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();

    [JsonIgnore] public CallerContext Caller { get; set; } = new(string.Empty, false);
}
