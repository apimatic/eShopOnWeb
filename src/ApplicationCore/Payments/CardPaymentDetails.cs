using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card details in transit to PayPal only. Never persist or log this type.
/// </summary>
public sealed class CardPaymentDetails
{
    public CardPaymentDetails(
        string number,
        string expiry,
        string? securityCode,
        string name,
        CardBillingAddress billingAddress)
    {
        Guard.Against.NullOrEmpty(number, nameof(number));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.Null(billingAddress, nameof(billingAddress));

        Number = number.Replace(" ", string.Empty, StringComparison.Ordinal);
        Expiry = expiry;
        SecurityCode = securityCode;
        Name = name;
        BillingAddress = billingAddress;
    }

    public string Number { get; }
    public string Expiry { get; }
    public string? SecurityCode { get; }
    public string Name { get; }
    public CardBillingAddress BillingAddress { get; }

    public override string ToString() => "CardPaymentDetails";
}

public sealed class CardBillingAddress
{
    public CardBillingAddress(
        string addressLine1,
        string? addressLine2,
        string adminArea1,
        string adminArea2,
        string postalCode,
        string countryCode)
    {
        Guard.Against.NullOrEmpty(addressLine1, nameof(addressLine1));
        Guard.Against.NullOrEmpty(adminArea2, nameof(adminArea2));
        Guard.Against.NullOrEmpty(postalCode, nameof(postalCode));
        Guard.Against.NullOrEmpty(countryCode, nameof(countryCode));

        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        AdminArea1 = adminArea1;
        AdminArea2 = adminArea2;
        PostalCode = postalCode;
        CountryCode = countryCode;
    }

    public string AddressLine1 { get; }
    public string? AddressLine2 { get; }
    public string AdminArea1 { get; }
    public string AdminArea2 { get; }
    public string PostalCode { get; }
    public string CountryCode { get; }
}
