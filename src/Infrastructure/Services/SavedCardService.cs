using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Saves, lists, resolves and removes a shopper's vaulted cards. Every operation is scoped to the owning
/// shopper; only the PayPal vault token id and a safe display (brand/last-four/expiry) are stored — never the PAN or CVV.
/// </summary>
public sealed class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _repository;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> repository, IPayPalGateway gateway, ILogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardInfo> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        var idempotencyKey = $"vault-{Guid.NewGuid():N}";
        var vaulted = await _gateway.VaultCardAsync(buyerId, card, idempotencyKey, ct);

        var saved = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        try
        {
            await _repository.AddAsync(saved, ct);
        }
        catch (DbUpdateException)
        {
            var existing = (await _repository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct))
                .FirstOrDefault(c => c.PayPalVaultId == vaulted.VaultId);
            if (existing is not null)
                return ToInfo(existing);
            throw;
        }

        _logger.LogInformation("Saved card {PaymentMethodId} for {BuyerId} ({Brand} ****{Last4})",
            saved.Id, buyerId, vaulted.Brand, vaulted.LastDigits);
        return ToInfo(saved);
    }

    public async Task<IReadOnlyList<SavedCardInfo>> ListAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _repository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
        return cards.Select(ToInfo).ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _repository.FirstOrDefaultAsync(new SavedCardByIdSpec(buyerId, paymentMethodId), ct)
            ?? throw PaymentException.NotFound($"Saved card {paymentMethodId} was not found.");

        // Remove locally first so it can no longer appear or be resolved for payment, then remove from the vault.
        await _repository.DeleteAsync(card, ct);

        try
        {
            await _gateway.DeleteVaultCardAsync(card.PayPalVaultId, ct);
        }
        catch (PaymentException ex)
        {
            // The local record is already gone (card is unusable); a lingering vault token is only a cleanup concern.
            _logger.LogWarning(ex, "Removed saved card {PaymentMethodId} locally but could not delete its vault token", paymentMethodId);
        }

        _logger.LogInformation("Deleted saved card {PaymentMethodId} for {BuyerId}", paymentMethodId, buyerId);
    }

    public async Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _repository.FirstOrDefaultAsync(new SavedCardByIdSpec(buyerId, paymentMethodId), ct)
            ?? throw PaymentException.NotFound($"Saved card {paymentMethodId} was not found.");
        return card.PayPalVaultId;
    }

    private static SavedCardInfo ToInfo(SavedCard c) =>
        new(c.Id, c.Brand, c.LastDigits, c.Expiry, c.CardholderName, c.CreatedAt);
}
