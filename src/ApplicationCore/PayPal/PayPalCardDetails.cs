namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

// A one-off card presented directly to PayPal. Never persisted by this app; only PayPal's
// own vault token id (see PayPalVaultedCard) is stored.
public class PayPalCardDetails
{
    public PayPalCardDetails(string cardholderName, string number, string expiry, string securityCode, PayPalAddress billingAddress)
    {
        CardholderName = cardholderName;
        Number = number;
        Expiry = expiry; // YYYY-MM
        SecurityCode = securityCode;
        BillingAddress = billingAddress;
    }

    public string CardholderName { get; }
    public string Number { get; }
    public string Expiry { get; }
    public string SecurityCode { get; }
    public PayPalAddress BillingAddress { get; }
}
