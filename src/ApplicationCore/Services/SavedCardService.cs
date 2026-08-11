using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's vaulted cards. The card itself is vaulted at PayPal; the app
/// keeps only the vault token and a safe descriptor. Every operation is scoped to the owning shopper.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> repository, IPayPalPaymentGateway gateway, IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardSummary> SaveCardAsync(string buyerId, CardDetails card, string? label, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // The vault id/request id is tied to the buyer so a retry does not create a duplicate token.
        var vaulted = await _gateway.VaultCardAsync(card, buyerId, $"vault-{buyerId}-{Guid.NewGuid():N}", ct);

        var saved = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.ExpiryMonth, vaulted.ExpiryYear, label);
        await _repository.AddAsync(saved, ct);

        _logger.LogInformation($"Saved card for {buyerId}: {vaulted.Brand} ****{vaulted.Last4} (id {saved.Id}).");
        return ToSummary(saved);
    }

    public async Task<IReadOnlyList<SavedCardSummary>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _repository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
        return cards.Select(ToSummary).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        // Scoped lookup: another shopper's card id will not be found here.
        var card = await _repository.FirstOrDefaultAsync(new SavedCardByIdForBuyerSpec(buyerId, paymentMethodId), ct);
        if (card == null)
        {
            return false;
        }

        // Remove from the PayPal vault first so the token can no longer be used to pay, then locally.
        await _gateway.DeleteVaultedCardAsync(card.VaultId, ct);
        await _repository.DeleteAsync(card, ct);

        _logger.LogInformation($"Removed saved card {paymentMethodId} for {buyerId}.");
        return true;
    }

    private static SavedCardSummary ToSummary(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        Expiry = card.ExpiryMonth != null && card.ExpiryYear != null ? $"{card.ExpiryYear}-{card.ExpiryMonth}" : null,
        Label = card.Label,
        CreatedAt = card.CreatedAt
    };
}
