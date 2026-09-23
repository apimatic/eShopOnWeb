using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Manages a shopper's vaulted cards. The local <c>SavedCards</c> store is authoritative for ownership:
/// listing, using (to pay), and deleting are all scoped to the caller, so one shopper never sees or acts on
/// another's card. Full card details are never stored — only the vault token and a safe descriptor.
/// </summary>
public sealed class SavedCardService : ISavedCardService
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

    public async Task<SavedCardView> SaveAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
            throw new PaymentValidationException("Card number and expiry are required to save a card.");

        // Group new tokens under the same PayPal customer this buyer already uses, if any.
        var existing = await _savedCards.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
        var existingCustomerId = existing
            .Select(c => c.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        var result = await _gateway.VaultCardAsync(card, buyerId, existingCustomerId, Guid.NewGuid().ToString("N"), ct);

        var saved = new SavedCard(buyerId, result.TokenId, result.CustomerId,
            result.Brand, result.LastDigits, result.Expiry, result.CardholderName ?? card.CardholderName);
        saved = await _savedCards.AddAsync(saved, ct);

        _logger.LogInformation("Saved a {Brand} card ending {Last4} for buyer.", saved.Brand, saved.LastDigits);
        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _savedCards.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
        return cards.Select(ToView).ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _savedCards.FirstOrDefaultAsync(new SavedCardByIdForBuyerSpec(paymentMethodId, buyerId), ct);
        if (card is null)
            throw new PaymentNotFoundException($"Saved card {paymentMethodId} was not found.");

        // Remove it from the PayPal vault first so the token can no longer be used, then from our store.
        await _gateway.DeleteVaultTokenAsync(card.PayPalVaultTokenId, ct);
        await _savedCards.DeleteAsync(card, ct);
        _logger.LogInformation("Deleted saved card {PaymentMethodId} for buyer.", paymentMethodId);
    }

    private static SavedCardView ToView(SavedCard c) =>
        new(c.Id, c.Brand, c.LastDigits, c.Expiry, c.CardholderName);
}
