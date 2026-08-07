namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Source of funds for a card payment: either raw card details or a previously vaulted card.</summary>
public abstract record CardPaymentSource;

/// <summary>A one-off payment using card details supplied with the request.</summary>
public sealed record RawCardSource(CardDetails Card) : CardPaymentSource;

/// <summary>A payment using a card saved in PayPal's vault, referenced by its vault token id.</summary>
public sealed record VaultedCardSource(string VaultId) : CardPaymentSource;

/// <summary>Outcome of a PayPal capture (create order + capture) for an order's payment.</summary>
public record CapturedPayment(string PayPalOrderId, string CaptureId, string Status)
{
    public bool IsCompleted => string.Equals(Status, "COMPLETED", System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>Outcome of a full refund against a capture.</summary>
public record RefundOutcome(string RefundId, string Status)
{
    public bool IsCompleted => string.Equals(Status, "COMPLETED", System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>A card that has been stored in PayPal's vault, with a safe descriptor for display.</summary>
public record VaultedCard(string VaultId, string? CardBrand, string? LastFourDigits, string? CardholderName, string? Expiry);
