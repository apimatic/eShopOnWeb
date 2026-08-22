using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardPaymentDetails ToDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        Name = Name,
        BillingAddress = BillingAddress == null
            ? null
            : new CardBillingAddress
            {
                AddressLine1 = BillingAddress.AddressLine1,
                AddressLine2 = BillingAddress.AddressLine2,
                AdminArea2 = BillingAddress.AdminArea2,
                AdminArea1 = BillingAddress.AdminArea1,
                PostalCode = BillingAddress.PostalCode,
                CountryCode = string.IsNullOrWhiteSpace(BillingAddress.CountryCode)
                    ? "US"
                    : BillingAddress.CountryCode
            }
    };
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
