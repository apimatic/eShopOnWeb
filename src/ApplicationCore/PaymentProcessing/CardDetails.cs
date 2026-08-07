namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// Raw card details supplied by a shopper for a one-off payment or to be vaulted. These are
/// forwarded straight to PayPal and are never persisted or logged by this application.
/// </summary>
public record CardDetails
{
    /// <summary>Primary account number, 13-19 digits (spec: card.number).</summary>
    public required string Number { get; init; }

    /// <summary>Expiry in <c>YYYY-MM</c> form (spec: card.expiry / date_year_month).</summary>
    public required string Expiry { get; init; }

    /// <summary>3-4 digit card security code (spec: card.security_code).</summary>
    public required string SecurityCode { get; init; }

    /// <summary>Cardholder name as printed on the card (spec: card.name).</summary>
    public string? Name { get; init; }

    // Optional billing address (spec: card.billing_address, an Address with a required country_code).
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingAdminArea1 { get; init; }
    public string? BillingAdminArea2 { get; init; }
    public string? BillingPostalCode { get; init; }

    /// <summary>Two-letter country code for the billing address (spec: country_code).</summary>
    public string? BillingCountryCode { get; init; }
}
