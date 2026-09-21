using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Saves, lists and removes a shopper's reusable cards. Vaulting happens at PayPal; only the token
/// id and a safe descriptor (brand, last 4, expiry) are stored locally. A card belongs to the
/// shopper who saved it — another shopper can neither see, use, nor delete it.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _repository;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> repository, IPayPalGateway gateway,
        ILogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        var vaulted = await _gateway.VaultCardAsync(new PayPalVaultCardRequest(buyerId, card), ct);

        var savedCard = new SavedCard(buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry);
        try
        {
            await _repository.AddAsync(savedCard, ct);
        }
        catch (DbUpdateException)
        {
            // Same vault token already recorded for this shopper — return the existing record.
            var existing = (await _repository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct))
                .FirstOrDefault(c => c.PayPalVaultId == vaulted.VaultId);
            if (existing is not null) return ToView(existing);
            throw;
        }

        _logger.LogInformation("Saved card {CardId} (vault token) for shopper.", savedCard.Id);
        return ToView(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _repository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
        return cards.Select(ToView).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _repository.GetByIdAsync(paymentMethodId, ct);
        // Not found, or belongs to another shopper: report as not found so ownership is not leaked.
        if (card is null || card.BuyerId != buyerId)
            return false;

        try
        {
            await _gateway.DeleteVaultTokenAsync(card.PayPalVaultId, ct);
        }
        catch (PayPalException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at PayPal — proceed to remove the local record so it can no longer be used.
            _logger.LogInformation("Vault token for card {CardId} was already absent at PayPal.", paymentMethodId);
        }

        await _repository.DeleteAsync(card, ct);
        _logger.LogInformation("Deleted saved card {CardId}.", paymentMethodId);
        return true;
    }

    private static SavedCardView ToView(SavedCard c) =>
        new(c.Id, c.Brand, c.Last4, c.Expiry, c.CreatedAt);
}
