using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderLineRequest> Items { get; set; } = new();
    public OrderAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class PayOrderRequest : BaseRequest
{
    public CardDetailsRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardBillingAddressRequest? BillingAddress { get; set; }

    public CardPaymentSource ToCardPaymentSource()
    {
        CardBillingAddress? billing = null;
        if (BillingAddress != null)
        {
            billing = new CardBillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.AddressLine2,
                BillingAddress.AdminArea2,
                BillingAddress.AdminArea1,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode);
        }

        return new CardPaymentSource(Number, Expiry, SecurityCode, Name, billing);
    }
}

public class CardBillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea2 { get; set; } = string.Empty;
    public string AdminArea1 { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
