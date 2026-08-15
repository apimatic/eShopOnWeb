using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.VaultAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<VaultedCard> _vaultRepository;
    private readonly IPaymentGateway _gateway;

    public SavedCardService(IRepository<VaultedCard> vaultRepository, IPaymentGateway gateway)
    {
        _vaultRepository = vaultRepository;
        _gateway = gateway;
    }

    public async Task<VaultedCard> SaveCardAsync(string buyerId, CardDetails card, string? label, CancellationToken cancellationToken = default)
    {
        var vault = await _gateway.VaultCardAsync(card, buyerId, cancellationToken);
        var saved = new VaultedCard(buyerId, vault.VaultId, vault.Brand, vault.Last4, vault.Expiry, label);
        return await _vaultRepository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<VaultedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _vaultRepository.ListAsync(new VaultedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int cardId, CancellationToken cancellationToken = default)
    {
        var card = await _vaultRepository.FirstOrDefaultAsync(new VaultedCardByIdSpecification(cardId, buyerId), cancellationToken);
        if (card is null)
        {
            throw new NotFoundException($"Saved card {cardId} was not found.");
        }

        // Remove from PayPal's vault first so it can no longer be charged, then from our store.
        await _gateway.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        await _vaultRepository.DeleteAsync(card, cancellationToken);
    }
}
