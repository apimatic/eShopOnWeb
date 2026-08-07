namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied by the shopper for a one-off charge or to be vaulted. This object is a
/// pass-through to the payment gateway only: it is NEVER persisted in the application database and
/// NEVER written to logs.
/// </summary>
public class PaymentCard
{
    public PaymentCard(
        string number,
        int expiryMonth,
        int expiryYear,
        string securityCode,
        string? cardholderName = null,
        CardBillingAddress? billingAddress = null)
    {
        Number = number;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        SecurityCode = securityCode;
        CardholderName = cardholderName;
        BillingAddress = billingAddress;
    }

    public string Number { get; }
    public int ExpiryMonth { get; }
    public int ExpiryYear { get; }
    public string SecurityCode { get; }
    public string? CardholderName { get; }
    public CardBillingAddress? BillingAddress { get; }

    /// <summary>Expiry formatted as PayPal expects it: "YYYY-MM".</summary>
    public string ExpiryYearMonth => $"{ExpiryYear:D4}-{ExpiryMonth:D2}";
}

/// <summary>Optional billing address used for card AVS checks. Not persisted by the application.</summary>
public class CardBillingAddress
{
    public CardBillingAddress(string addressLine1, string? adminArea1, string? adminArea2,
        string? postalCode, string? countryCode)
    {
        AddressLine1 = addressLine1;
        AdminArea1 = adminArea1;
        AdminArea2 = adminArea2;
        PostalCode = postalCode;
        CountryCode = countryCode;
    }

    public string AddressLine1 { get; }
    public string? AdminArea1 { get; }   // state / province
    public string? AdminArea2 { get; }   // city / town
    public string? PostalCode { get; }
    public string? CountryCode { get; }  // 2-letter ISO
}
