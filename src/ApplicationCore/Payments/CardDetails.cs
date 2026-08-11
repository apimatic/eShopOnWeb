namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off (direct) card payment or to vault a card. These are passed
/// straight through to PayPal and are NEVER persisted in this app's database or written to logs.
/// </summary>
public class CardDetails
{
    public string Number { get; init; } = default!;

    /// <summary>Two-digit expiry month, e.g. "07".</summary>
    public string ExpiryMonth { get; init; } = default!;

    /// <summary>Four-digit expiry year, e.g. "2030".</summary>
    public string ExpiryYear { get; init; } = default!;

    public string SecurityCode { get; init; } = default!;

    public string? CardholderName { get; init; }

    // Billing address (optional but improves acceptance rates for card payments).
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAdminArea2 { get; init; } // city
    public string? BillingAdminArea1 { get; init; } // state/province
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; } // ISO-3166-1 alpha-2
}
