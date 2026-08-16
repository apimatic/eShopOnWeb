using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Manages a shopper's saved cards. The card itself is stored only in PayPal's vault; this app
/// keeps the vault token plus safe display metadata (brand, last four, expiry). Every operation
/// is scoped to the owning shopper so no shopper can see, use, or delete another's card.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCards;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCards,
        IPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCards = savedCards;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, customerId: null, cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry, label);
        await _savedCards.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Shopper {buyerId} saved a {vaulted.Brand} card ending {vaulted.Last4} (payment method {savedCard.Id}).");
        return ToView(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _savedCards.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards.Select(ToView).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var card = await _savedCards.GetByIdAsync(paymentMethodId, cancellationToken);
        if (card is null || card.BuyerId != buyerId)
        {
            // Not found or not owned — treat identically so ownership cannot be probed.
            return false;
        }

        // Remove from PayPal's vault first so the card can no longer be charged, then drop our record.
        await _gateway.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        await _savedCards.DeleteAsync(card, cancellationToken);

        _logger.LogInformation($"Shopper {buyerId} deleted saved payment method {paymentMethodId}.");
        return true;
    }

    private static SavedCardView ToView(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        Expiry = card.Expiry,
        Label = card.Label,
        CreatedAt = card.CreatedAt
    };
}
