namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>How a payment should be funded: a one-off card or a saved (vaulted) card.</summary>
public abstract record PaymentSourceSelection
{
    private PaymentSourceSelection() { }

    public sealed record OneOffCard(CardDetails Card) : PaymentSourceSelection;

    public sealed record SavedCard(int SavedPaymentMethodId) : PaymentSourceSelection;

    /// <summary>Internal: a saved card resolved to its processor vault token.</summary>
    public sealed record VaultedCardToken(string VaultTokenId) : PaymentSourceSelection;
}
