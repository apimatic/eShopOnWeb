using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Vaults cards at PayPal and stores only their safe descriptor. All reads and deletes are scoped to the
/// caller so one shopper can never see, use, or delete another's card.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCards;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> savedCards, IPayPalGateway gateway,
        ILogger<SavedCardService> logger)
    {
        _savedCards = savedCards;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Vault first: the vault id IS the local claim, and there is no eShop-side reference to pre-persist.
        var requestId = $"vault-{buyerId}-{Guid.NewGuid():N}";
        var result = await _gateway.CreateVaultCardAsync(
            new VaultCardRequest(card, buyerId), requestId, ct);

        var saved = new SavedCard(buyerId, result.VaultId, result.Brand, result.LastDigits,
            result.Expiry, result.CardholderName);
        try
        {
            await _savedCards.AddAsync(saved, ct);
        }
        catch (DbUpdateException)
        {
            // The same card was already vaulted+saved by this shopper — return the existing record.
            var existing = (await _savedCards.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct))
                .FirstOrDefault(c => c.PayPalVaultId == result.VaultId);
            if (existing is not null) return existing;
            throw;
        }

        _logger.LogInformation("Card saved for {BuyerId}: {Brand} ****{Last}", buyerId, result.Brand, result.LastDigits);
        return saved;
    }

    public async Task<IReadOnlyList<SavedCard>> GetCardsForBuyerAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _savedCards.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
        return cards.ToList();
    }

    public async Task<SavedCard?> GetCardForBuyerAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _savedCards.GetByIdAsync(paymentMethodId, ct);
        return card is not null && card.BuyerId == buyerId ? card : null;
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _savedCards.GetByIdAsync(paymentMethodId, ct);
        if (card is null || card.BuyerId != buyerId)
            return false;

        // Delete at PayPal first so the card can no longer be used to pay, then remove the local record.
        await _gateway.DeleteVaultCardAsync(card.PayPalVaultId, ct);
        await _savedCards.DeleteAsync(card, ct);

        _logger.LogInformation("Card {PaymentMethodId} removed for {BuyerId}", paymentMethodId, buyerId);
        return true;
    }
}
