using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _paymentGateway.VaultCardAsync(card, buyerId,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}", cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.LastDigits, vaulted.Brand, vaulted.Expiry);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved card ending in {vaulted.LastDigits} for shopper {buyerId}.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, cancellationToken);
        if (savedCard == null || savedCard.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Payment method {savedCardId} was not found.");
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PaymentException ex)
        {
            // The card is removed locally regardless; a token PayPal no longer knows is unusable anyway.
            _logger.LogWarning($"Deleting vault token for saved card {savedCardId} failed at the provider: {ex.Message}");
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
    }
}
