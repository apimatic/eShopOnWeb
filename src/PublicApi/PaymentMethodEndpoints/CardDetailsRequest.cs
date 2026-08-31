using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Full card details as supplied by the shopper. Transient: forwarded to PayPal and
/// never persisted or logged.
/// </summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public GatewayCardDetails ToGatewayModel() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddress = BillingAddress == null ? null : new GatewayAddress
        {
            AddressLine1 = BillingAddress.AddressLine1,
            AddressLine2 = BillingAddress.AddressLine2,
            City = BillingAddress.City,
            State = BillingAddress.State,
            PostalCode = BillingAddress.PostalCode,
            CountryCode = BillingAddress.CountryCode
        }
    };
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}
