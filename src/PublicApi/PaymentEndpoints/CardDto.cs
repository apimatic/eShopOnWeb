using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Card details supplied by the shopper for a one-off payment or to save a card. These are passed
/// straight through to the payment provider and are never persisted or logged by this app.
/// </summary>
public class CardDto
{
    /// <summary>Primary account number.</summary>
    public string? Number { get; set; }

    /// <summary>Expiry in YYYY-MM form (e.g. "2028-04"). Alternatively supply ExpiryMonth + ExpiryYear.</summary>
    public string? Expiry { get; set; }

    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }

    /// <summary>Card security code (CVV/CVC).</summary>
    public string? SecurityCode { get; set; }

    /// <summary>Cardholder name.</summary>
    public string? Name { get; set; }

    public BillingAddressDto? BillingAddress { get; set; }

    public GatewayCardDetails ToGatewayCard()
    {
        if (string.IsNullOrWhiteSpace(Number))
        {
            throw new PaymentOperationException("Card number is required.");
        }
        if (string.IsNullOrWhiteSpace(SecurityCode))
        {
            throw new PaymentOperationException("Card security code is required.");
        }

        var expiry = ResolveExpiry();

        GatewayBillingAddress? billing = null;
        if (BillingAddress is not null)
        {
            if (string.IsNullOrWhiteSpace(BillingAddress.CountryCode))
            {
                throw new PaymentOperationException("Billing address country code is required when a billing address is supplied.");
            }
            billing = new GatewayBillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.AddressLine2,
                BillingAddress.AdminArea2,
                BillingAddress.AdminArea1,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode!);
        }

        return new GatewayCardDetails(Number.Replace(" ", string.Empty), expiry, SecurityCode, Name, billing);
    }

    private string ResolveExpiry()
    {
        if (!string.IsNullOrWhiteSpace(Expiry))
        {
            return Expiry.Trim();
        }
        if (ExpiryMonth is >= 1 and <= 12 && ExpiryYear is >= 2000 and <= 9999)
        {
            return $"{ExpiryYear.Value.ToString("D4", CultureInfo.InvariantCulture)}-{ExpiryMonth.Value.ToString("D2", CultureInfo.InvariantCulture)}";
        }
        throw new PaymentOperationException("Card expiry is required as 'Expiry' (YYYY-MM) or 'ExpiryMonth' and 'ExpiryYear'.");
    }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>City / town.</summary>
    public string? AdminArea2 { get; set; }
    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>ISO-3166-1 alpha-2 country code (required if a billing address is supplied).</summary>
    public string? CountryCode { get; set; }
}
