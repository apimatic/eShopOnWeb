using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record VaultedCardResult(
    string VaultPaymentTokenId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

/// <summary>
/// Vaults cards at the payment provider for later reuse. Only the provider's token and safe
/// display data ever come back.
/// </summary>
public interface ICardVault
{
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string merchantCustomerId, string? payPalCustomerId, string requestKey, CancellationToken ct);
    Task DeleteCardAsync(string vaultPaymentTokenId, CancellationToken ct);
}
