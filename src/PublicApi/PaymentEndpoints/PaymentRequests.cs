using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

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

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }

    /// <summary>Set server-side from the caller's token; never bound from the request body.</summary>
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderRequest
{
    /// <summary>Raw card for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>, not both.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead of a raw card.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key does not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Operator action against a single order (fulfil/cancel), identified by route.</summary>
public record OrderIdRequest(int OrderId);

public class SaveCardRequest
{
    public CardDto Card { get; set; } = new();
    public string? Label { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}
