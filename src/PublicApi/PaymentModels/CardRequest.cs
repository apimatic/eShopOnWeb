using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Card details supplied by the caller for a one-off charge or to be saved. These values are passed
/// straight through to PayPal and are never stored by the application or written to logs.
/// </summary>
public class CardRequest
{
    /// <summary>Full card number (PAN). Test card for the sandbox: 4111 1111 1111 1111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry month, 1-12.</summary>
    public int ExpiryMonth { get; set; }

    /// <summary>Four-digit expiry year, e.g. 2030.</summary>
    public int ExpiryYear { get; set; }

    /// <summary>Card security code (CVC/CVV).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Cardholder name (optional).</summary>
    public string? CardholderName { get; set; }

    /// <summary>Billing address (optional; used for AVS).</summary>
    public BillingAddressRequest? BillingAddress { get; set; }

    public PaymentCard ToDomain()
    {
        CardBillingAddress? billing = BillingAddress is null
            ? null
            : new CardBillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.State,
                BillingAddress.City,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode);

        return new PaymentCard(
            Number,
            ExpiryMonth,
            ExpiryYear,
            SecurityCode,
            CardholderName,
            billing);
    }
}

public class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Two-letter ISO country code, e.g. "US".</summary>
    public string? CountryCode { get; set; }
}
