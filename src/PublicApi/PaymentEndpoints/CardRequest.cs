using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class BillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public sealed class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardInput ToCardInput()
    {
        return new CardInput
        {
            Number = Number,
            ExpirationMonth = ExpirationMonth,
            ExpirationYear = ExpirationYear,
            SecurityCode = SecurityCode,
            Name = Name,
            BillingAddress = BillingAddress == null
                ? null
                : new FinixAddress
                {
                    Line1 = BillingAddress.Line1,
                    Line2 = BillingAddress.Line2,
                    City = BillingAddress.City,
                    Region = BillingAddress.Region,
                    PostalCode = BillingAddress.PostalCode,
                    Country = BillingAddress.Country
                }
        };
    }
}
