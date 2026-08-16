using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied by a shopper. Never stored in this app's database and never logged.</summary>
public class CardDto
{
    /// <summary>Card number (e.g. sandbox Visa 4111111111111111).</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in PayPal's <c>YYYY-MM</c> form, e.g. "2030-01".</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardPaymentDetails ToCardPaymentDetails() =>
        new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToPayPalBillingAddress());
}

public class BillingAddressDto
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public PayPalBillingAddress ToPayPalBillingAddress() =>
        new(Line1, Line2, City, State, PostalCode, CountryCode);
}

/// <summary>Optional shipping address for a placed order.</summary>
public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
