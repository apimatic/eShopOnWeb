namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Full card details as entered by the shopper. Held in memory only for the duration of one request:
/// never persisted, never logged (<see cref="ToString"/> is redacted on purpose).
/// </summary>
public sealed class CardDetails
{
    public CardDetails(string number, string expiry, string? securityCode, string? nameOnCard, CardBillingAddress? billingAddress)
    {
        Number = number;
        Expiry = expiry;
        SecurityCode = securityCode;
        NameOnCard = nameOnCard;
        BillingAddress = billingAddress;
    }

    public string Number { get; }
    /// <summary>ISO-8601 year-month, <c>YYYY-MM</c>.</summary>
    public string Expiry { get; }
    public string? SecurityCode { get; }
    public string? NameOnCard { get; }
    public CardBillingAddress? BillingAddress { get; }

    public string LastDigits => Number.Length >= 4 ? Number[^4..] : Number;

    public override string ToString() => $"Card ****{LastDigits} exp {Expiry}";
}

public sealed record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);
