using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPal.VaultCardAsync(card, cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultId, vaulted.Last4, vaulted.Brand,
            vaulted.ExpiryMonth, vaulted.ExpiryYear, label);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved card {savedCard.Id} (vault {vaulted.VaultId}) for buyer.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _savedCardRepository.ListAsync(
            new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task<bool> RemoveCardAsync(string buyerId, int savedCardId,
        CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdSpecification(savedCardId, buyerId), cancellationToken);
        if (savedCard is null)
        {
            return false; // Missing, or not the caller's card.
        }

        // Delete from PayPal's vault first so the card can no longer be used to pay, then drop our record.
        await _payPal.DeleteVaultedCardAsync(savedCard.VaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Removed saved card {savedCardId} (vault {savedCard.VaultId}).");
        return true;
    }
}
