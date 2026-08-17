using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Shared request models ----

/// <summary>Card details supplied by the caller. Transient only — never stored or logged by this app.</summary>
public class CardModel
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as "YYYY-MM" or "MM/YY". Any future date is accepted.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressModel? BillingAddress { get; set; }
}

public class BillingAddressModel
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PlaceOrderItemModel
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressModel
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

// ---- Request bodies (identity/route values are set server-side, never bound from the body) ----

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemModel> Items { get; set; } = new();
    public ShippingAddressModel? Shipping { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderRequest : BaseRequest
{
    public CardModel? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Reason { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressModel? BillingAddress { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

// Route-only request markers for endpoints without a body.
public record FulfilOrderRequest(int OrderId);
public record CancelOrderRequest(int OrderId);
public record MyOrdersRequest(string BuyerId);
public record ReconciliationRequest(string? From, string? To);
public record ListPaymentMethodsRequest(string BuyerId);
public record DeletePaymentMethodRequest(int PaymentMethodId, string BuyerId);

// ---- Response bodies whose top-level id field the task requires ----

public class PlaceOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}
