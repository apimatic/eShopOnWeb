using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> savedCardRepository,
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

        var vaulted = await _gateway.VaultCardAsync(card, $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}", cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.CustomerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved card {savedCard.Id} ({vaulted.Brand} ending {vaulted.LastDigits}) vaulted for buyer {buyerId}.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId), cancellationToken);
        if (savedCard == null || savedCard.BuyerId != buyerId)
            throw new EntityNotFoundException($"Payment method {savedCardId} not found.");

        try
        {
            await _gateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // A token already gone at the provider must not block local removal.
            _logger.LogWarning($"Provider delete of vault token for saved card {savedCardId} failed: {ex.Message}. Removing locally.");
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        _logger.LogInformation($"Saved card {savedCardId} deleted for buyer {buyerId}.");
    }
}
