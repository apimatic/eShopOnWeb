namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class CardBillingAddress
{
    public string CountryCode { get; init; } = string.Empty;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
}

/// <summary>
/// Card details in transit to PayPal. Never persist or log this type.
/// </summary>
public sealed class CardPaymentSource
{
    public string Number { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string SecurityCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public CardBillingAddress? BillingAddress { get; init; }

    public string LastDigits => Number.Length >= 4 ? Number[^4..] : string.Empty;

    public override string ToString() => $"[redacted card ending {LastDigits}]";
}
