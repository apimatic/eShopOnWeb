using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
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
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public ApplicationCore.Interfaces.CardPaymentRequest ToCardPaymentRequest() =>
        new(Number, Expiry, SecurityCode, Name,
            BillingAddress is null
                ? null
                : new ApplicationCore.Interfaces.BillingAddressRequest(
                    BillingAddress.AddressLine1,
                    BillingAddress.AddressLine2,
                    BillingAddress.AdminArea2,
                    BillingAddress.AdminArea1,
                    BillingAddress.PostalCode,
                    BillingAddress.CountryCode));
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class RefundOrderRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}
