using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card details for a one-off PayPal charge. Never persist or log this type;
/// <see cref="ToString"/> redacts the PAN and CVC.
/// </summary>
public sealed class CardPaymentDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }

    public string LastDigits =>
        Number.Length >= 4 ? Number[^4..] : "****";

    public override string ToString() =>
        $"CardPaymentDetails {{ LastDigits = {LastDigits}, Expiry = {Expiry}, Name = {Name} }}";
}

public sealed class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}
