using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardPaymentDetails ToDetails() =>
        new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToDetails());
}

public class BillingAddressRequest
{
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }

    public CardBillingAddress ToDetails() =>
        new(CountryCode, AddressLine1, AddressLine2, AdminArea2, AdminArea1, PostalCode);
}
