namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied for a one-off payment or to be vaulted. This type is transient: it
/// is passed straight to the PayPal gateway and never persisted in this app's database.
/// <see cref="ToString"/> is deliberately redacted so a card can never leak into logs.
/// </summary>
public sealed record CardDetails
{
    /// <summary>Primary account number (PAN), digits only, e.g. "4111111111111111".</summary>
    public required string Number { get; init; }

    /// <summary>Expiry in "YYYY-MM" format, e.g. "2030-01".</summary>
    public required string Expiry { get; init; }

    /// <summary>Card verification value (CVV/CVC).</summary>
    public required string SecurityCode { get; init; }

    /// <summary>Cardholder name as printed on the card.</summary>
    public string? CardholderName { get; init; }

    public CardBillingAddress? BillingAddress { get; init; }

    /// <summary>Best-effort last four digits, for building a safe descriptor. Empty if unknown.</summary>
    public string Last4 => Number is { Length: >= 4 } ? Number[^4..] : string.Empty;

    // Redacted: never expose the PAN/CVV via ToString (records would otherwise print every member).
    public override string ToString() => $"CardDetails {{ Number = ****{Last4}, Expiry = ****, SecurityCode = *** }}";
}

/// <summary>Optional billing address for a card. All fields optional except country code (per PayPal).</summary>
public sealed record CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }

    /// <summary>Two-letter ISO country code, e.g. "US".</summary>
    public required string CountryCode { get; init; }
}
