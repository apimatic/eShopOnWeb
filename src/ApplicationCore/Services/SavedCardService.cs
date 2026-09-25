using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's vaulted cards. A saved card belongs to the shopper who
/// saved it; every operation is scoped to the caller so one shopper can never see, use, or delete
/// another's. Only the vault token id and a safe descriptor are stored — never full card details.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        // Fresh idempotency base per save; the gateway derives the setup/token request ids from it so
        // the SDK's own retries of this two-step vaulting cannot create duplicates.
        var vaulted = await _gateway.VaultCardAsync(new VaultCardRequest(card, buyerId),
            idempotencyKeyBase: Guid.NewGuid().ToString("N"), cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry);
        saved = await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation($"Saved card for buyer: paymentMethodId={saved.Id} brand={vaulted.Brand} last4={vaulted.Last4}");
        return new SavedCardView(saved.Id, saved.CardBrand, saved.CardLast4, saved.CardExpiry, saved.CreatedAt);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return cards
            .Select(c => new SavedCardView(c.Id, c.CardBrand, c.CardLast4, c.CardExpiry, c.CreatedAt))
            .ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var card = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (card is null) return false;

        // Revoke at PayPal first, then drop locally, so a deleted card can no longer be used to pay.
        await _gateway.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        await _repository.DeleteAsync(card, cancellationToken);

        _logger.LogInformation($"Deleted saved card paymentMethodId={paymentMethodId} for buyer.");
        return true;
    }
}
