using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
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

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(
            card, merchantCustomerId: buyerId, idempotencyKey: $"eshop-vault-{buyerId}-{Guid.NewGuid():N}", ct);

        var savedCard = new SavedCard(buyerId, vaulted.PaymentTokenId, vaulted.CustomerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry);
        await _savedCardRepository.AddAsync(savedCard, ct);

        _logger.LogInformation($"Buyer saved a {vaulted.Brand} card ending in {vaulted.LastDigits}.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken ct)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new OrderStateException($"Saved card {savedCardId} was not found.");
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(savedCard.PayPalPaymentTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404)
        {
            // Already gone from the vault; still remove it locally so it can never be used.
        }

        await _savedCardRepository.DeleteAsync(savedCard, ct);
        _logger.LogInformation($"Buyer deleted saved card {savedCardId}.");
    }
}
