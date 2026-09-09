using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Base for shopper-scoped requests. <see cref="CallerBuyerId"/> is set server-side from the JWT
/// after model binding, so any value a client puts in the body is ignored — identity comes from the
/// token, never the payload.
/// </summary>
public abstract class ShopperRequest : BaseRequest
{
    [JsonIgnore]
    public string CallerBuyerId { get; set; } = string.Empty;
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class PlaceOrderRequest : ShopperRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();

    /// <summary>Optional; a placeholder is used when omitted since shipping is out of scope here.</summary>
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class PayOrderRequest : ShopperRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>One-off card details. Provide this OR <see cref="SavedCardId"/>, not both.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with.</summary>
    public int? SavedCardId { get; set; }
}

public class RefundOrderRequest : ShopperRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Amount to refund; when omitted, the full remaining refundable amount is refunded.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: a repeat under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest : ShopperRequest
{
    public CardRequest Card { get; set; } = new();

    /// <summary>Optional shopper-chosen label for the saved card.</summary>
    public string? Alias { get; set; }
}
