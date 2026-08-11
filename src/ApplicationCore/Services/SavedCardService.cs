using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> repository, IPayPalGateway payPal, IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number))
        {
            throw new ValidationException("Card details are required to save a card.");
        }

        var vaulted = await _payPal.VaultCardAsync(card, cancellationToken);
        var saved = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastFourDigits,
            vaulted.ExpiryMonth, vaulted.ExpiryYear, card.CardholderName);
        saved = await _repository.AddAsync(saved, cancellationToken);

        // Never log card details — only the safe description and vault id.
        _logger.LogInformation($"Saved card {saved.Id} for buyer {buyerId}: {saved.Brand} ****{saved.LastFourDigits}, vault {saved.VaultId}.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int cardId, CancellationToken cancellationToken = default)
    {
        // Scoped to the owner: one shopper can never delete another's card.
        var card = await _repository.FirstOrDefaultAsync(new SavedCardByIdSpecification(cardId, buyerId), cancellationToken)
            ?? throw new NotFoundException($"Saved card {cardId} was not found.");

        await _payPal.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        await _repository.DeleteAsync(card, cancellationToken);

        _logger.LogInformation($"Removed saved card {cardId} for buyer {buyerId} (vault {card.VaultId}); it can no longer be used to pay.");
    }
}
