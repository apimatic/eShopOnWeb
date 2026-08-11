using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalClient payPalClient,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, PayPalCardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // The card is vaulted with PayPal; only the returned token and safe descriptors are stored locally.
        var idempotencyKey = $"vault-{buyerId}-{System.Guid.NewGuid():N}";
        var vaulted = await _payPalClient.VaultCardAsync(card, MerchantCustomerId(buyerId), idempotencyKey, cancellationToken);

        var savedCard = new SavedCard(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.Name);

        savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);
        _logger.LogInformation($"Saved card {savedCard.Id} ({savedCard.Description}) for buyer.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task<SavedCard?> GetOwnedCardAsync(string buyerId, int savedCardId,
        CancellationToken cancellationToken = default)
    {
        var card = await _savedCardRepository.GetByIdAsync(savedCardId, cancellationToken);
        return card is not null && card.BuyerId == buyerId ? card : null;
    }

    public async Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        var card = await GetOwnedCardAsync(buyerId, savedCardId, cancellationToken);
        if (card is null)
        {
            // Not found or not the caller's — treated the same so existence is never revealed across shoppers.
            throw new PaymentResourceNotFoundException($"Saved card {savedCardId} was not found.");
        }

        // Delete the vault token first so the card can no longer be charged even if the local delete were to fail.
        await _payPalClient.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation($"Deleted saved card {savedCardId}.");
    }

    // PayPal's merchant_customer_id accepts [0-9a-zA-Z-_.^*$@#]; keep only those characters from the identity.
    private static string MerchantCustomerId(string buyerId)
    {
        var cleaned = new string(buyerId.Where(c =>
            char.IsLetterOrDigit(c) || "-_.^*$@#".IndexOf(c) >= 0).ToArray());
        if (cleaned.Length == 0) cleaned = "customer";
        return cleaned.Length > 64 ? cleaned.Substring(0, 64) : cleaned;
    }
}
