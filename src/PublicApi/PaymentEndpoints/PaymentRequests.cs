using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressDto? ShipTo { get; set; }

    // Set from the JWT server-side; never bound from the body.
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>The id of one of the caller's saved cards. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for the full remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}
