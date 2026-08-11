using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Raw card details supplied on a request. Never stored in the app database, never logged.</summary>
public class CardInput
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Two-digit month, e.g. "11".</summary>
    public string ExpiryMonth { get; set; } = string.Empty;
    /// <summary>Four-digit (or two-digit) year, e.g. "2027".</summary>
    public string ExpiryYear { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressInput? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number?.Replace(" ", string.Empty).Trim() ?? string.Empty,
        ExpiryMonth?.Trim() ?? string.Empty,
        ExpiryYear?.Trim() ?? string.Empty,
        SecurityCode?.Trim() ?? string.Empty,
        CardholderName,
        BillingAddress?.ToBillingAddress());
}

/// <summary>Billing address that accompanies a raw card.</summary>
public class BillingAddressInput
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    /// <summary>City.</summary>
    public string City { get; set; } = string.Empty;
    /// <summary>State / province.</summary>
    public string? State { get; set; }
    /// <summary>ISO-3166-1 alpha-2 country code, e.g. "US".</summary>
    public string CountryCode { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;

    public BillingAddress ToBillingAddress() => new(
        AddressLine1, AddressLine2, City, State, CountryCode, PostalCode);
}
