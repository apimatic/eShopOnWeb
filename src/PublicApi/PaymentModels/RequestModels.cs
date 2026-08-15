using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public class OrderLineModel
{
    [Required]
    public int CatalogItemId { get; set; }

    [Required]
    public int Quantity { get; set; }
}

public class ShipToAddressModel
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class PlaceOrderRequest
{
    [Required]
    public List<OrderLineModel> Items { get; set; } = new();

    /// <summary>Optional shipping address. The additive API flow does not require one.</summary>
    public ShipToAddressModel? ShipTo { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Raw card details for a one-off payment. Provide this OR <see cref="SavedCardId"/>, not both.</summary>
    public CardModel? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedCardId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    [Required]
    public CardModel Card { get; set; } = new();

    /// <summary>Optional label to help the shopper recognise the card.</summary>
    public string? Alias { get; set; }
}
