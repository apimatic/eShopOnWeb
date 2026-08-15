namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// Raw card details supplied transiently by a caller to pay a one-off order or to vault a card.
/// This type is NEVER persisted to the application's own database and NEVER written to logs — it
/// exists only to be handed to the PayPal gateway for the duration of a single request.
/// </summary>
public sealed class CardDetails
{
    /// <summary>Full card number (PAN). Passed to PayPal only; never stored.</summary>
    public string Number { get; init; } = default!;

    /// <summary>Expiry month, 1-12.</summary>
    public int ExpiryMonth { get; init; }

    /// <summary>Four-digit expiry year, e.g. 2030.</summary>
    public int ExpiryYear { get; init; }

    /// <summary>Card security code (CVC/CVV). Passed to PayPal only; never stored.</summary>
    public string SecurityCode { get; init; } = default!;

    /// <summary>Name printed on the card.</summary>
    public string? CardholderName { get; init; }

    // Billing address (optional; improves card acceptance).
    public string? CountryCode { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea1 { get; init; } // state / province
    public string? AdminArea2 { get; init; } // city / locality
    public string? PostalCode { get; init; }

    /// <summary>PayPal expects expiry formatted as "YYYY-MM".</summary>
    public string ToPayPalExpiry() => $"{ExpiryYear:D4}-{ExpiryMonth:D2}";
}
