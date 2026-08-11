using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Place an order from catalog items. Amounts are taken from catalog prices.</summary>
public class PlaceOrderRequest
{
    public List<OrderLineInput> Items { get; set; } = new();

    /// <summary>Optional shipping address; a default is used when omitted.</summary>
    public ShippingAddressInput? ShipToAddress { get; set; }

    /// <summary>Set server-side from the token; never bound from the request body.</summary>
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class OrderLineInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressInput
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Pay (authorize) an order with a one-off card or one of the shopper's saved cards.</summary>
public class PayOrderRequest
{
    /// <summary>Raw card details for a one-off payment. Provide this OR <see cref="SavedCardId"/>.</summary>
    public CardInput? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedCardId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Refund a captured order, full or partial, under a caller-supplied idempotency key.</summary>
public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Operator action addressed to a single order (fulfil / cancel). No body.</summary>
public class OrderIdRequest
{
    [JsonIgnore] public int OrderId { get; set; }
}

/// <summary>Save (vault) a card for the signed-in shopper.</summary>
public class SavePaymentMethodRequest
{
    public CardInput Card { get; set; } = new();

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}
