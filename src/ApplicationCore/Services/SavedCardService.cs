using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var customerId = PayPalCustomerId.ForBuyer(buyerId);
        var vaulted = await _gateway.SaveCardAsync(customerId, card, Guid.NewGuid().ToString("N"), cancellationToken);

        var savedCard = new SavedCard(buyerId, customerId, vaulted.VaultTokenId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Buyer vaulted a card ending {savedCard.LastDigits} (token {savedCard.VaultTokenId}).");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdSpecification(savedCardId), cancellationToken);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new SavedCardNotFoundException(savedCardId);
        }

        try
        {
            await _gateway.DeleteSavedCardAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone from PayPal's vault; still remove the local record.
            _logger.LogWarning($"Vault token {savedCard.VaultTokenId} was already absent at PayPal.");
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
    }
}
