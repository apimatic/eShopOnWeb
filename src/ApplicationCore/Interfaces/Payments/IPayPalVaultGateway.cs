using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over the PayPal Vault v3 API for saving and removing cards. Implemented in the
/// infrastructure layer against the PayPal OpenAPI contract.
/// </summary>
public interface IPayPalVaultGateway
{
    /// <summary>Vaults a raw card and returns the vault id plus a safe descriptor.</summary>
    Task<VaultedCard> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}
