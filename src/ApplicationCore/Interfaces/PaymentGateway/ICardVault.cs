using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>
/// Saving and removing cards in PayPal's vault (Vault Payment Tokens v3). Full card details
/// are handed to PayPal and never stored by this application.
/// </summary>
public interface ICardVault
{
    Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}
