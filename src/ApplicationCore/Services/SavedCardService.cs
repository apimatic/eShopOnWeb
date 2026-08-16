using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's cards (Flow 2). The card is vaulted with PayPal; this app
/// stores only the vault token id and a safe descriptor. All operations are scoped to the caller.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPalClient;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalClient payPalClient)
    {
        _savedCardRepository = savedCardRepository;
        _payPalClient = payPalClient;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPalClient.VaultCardAsync(card, Guid.NewGuid().ToString("N"), cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.TokenId, vaulted.CustomerId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry, label);

        return await _savedCardRepository.AddAsync(savedCard, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task<bool> DeleteAsync(string buyerId, int savedCardId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdForBuyerSpecification(savedCardId, buyerId), cancellationToken);
        if (card is null)
            return false;

        // Remove from PayPal's vault first so it can no longer be used to pay, then drop our record.
        await _payPalClient.DeleteVaultedCardAsync(card.VaultTokenId, cancellationToken);
        await _savedCardRepository.DeleteAsync(card, cancellationToken);
        return true;
    }
}
