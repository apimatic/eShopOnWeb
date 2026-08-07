namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>An amount to charge or refund. Currency is an ISO-4217 code (USD for this app).</summary>
public record PaymentAmount(decimal Value, string Currency = "USD");

/// <summary>
/// Raw card details supplied for a one-off charge or to be vaulted. This is a transient input only:
/// it is never persisted in the application's database and never written to logs.
/// </summary>
public record CardDetails
{
    /// <summary>Full card number (PAN).</summary>
    public required string Number { get; init; }

    /// <summary>Expiry in YYYY-MM form (as PayPal expects).</summary>
    public required string Expiry { get; init; }

    /// <summary>Card security code (CVC).</summary>
    public required string SecurityCode { get; init; }

    /// <summary>Cardholder name.</summary>
    public string? CardholderName { get; init; }

    /// <summary>Two-letter billing country code.</summary>
    public string? BillingCountryCode { get; init; }

    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
}

/// <summary>Outcome of a successful card charge + capture.</summary>
public record CardPaymentResult(string PayPalOrderId, string CaptureId);

/// <summary>Outcome of a refund.</summary>
public record RefundResult(string RefundId, string Status);

/// <summary>
/// A vaulted card, as it can be safely shown back to the shopper. Carries only the opaque
/// vault token plus a display-safe descriptor — never the PAN or CVC.
/// </summary>
public record VaultedCard(string VaultId, string Brand, string LastFourDigits, string Expiry);
