namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card details supplied by the caller for a one-off charge or vault. Never persist or log this type.
/// </summary>
public sealed class CardPaymentDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }

    public override string ToString() => "[redacted card details]";
}

public sealed class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }

    public override string ToString() => "[redacted billing address]";
}
