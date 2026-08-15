using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A card as supplied over the API. Transient: mapped straight to the gateway, never persisted or logged.</summary>
public class CardModel
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;      // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardDetails ToDetails() => new(
        Number, Expiry, SecurityCode, Name,
        AddressLine1, AddressLine2, City, State, PostalCode,
        string.IsNullOrWhiteSpace(CountryCode) ? "US" : CountryCode!);
}

public class OrderLineRequest
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
}

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public OrderPaymentView? Order { get; set; }
}

public class PayOrderRequest
{
    public CardModel? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public OrderPaymentView? Order { get; set; }
}

public class MyOrdersResponse
{
    public IReadOnlyList<OrderPaymentView> Orders { get; set; } = new List<OrderPaymentView>();
}

public class SaveCardResponse
{
    public int PaymentMethodId { get; set; }
    public SavedCardView? Card { get; set; }
}

public class ListCardsResponse
{
    public IReadOnlyList<SavedCardView> PaymentMethods { get; set; } = new List<SavedCardView>();
}
