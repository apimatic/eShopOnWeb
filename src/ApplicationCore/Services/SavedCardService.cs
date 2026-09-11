using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, GatewayCardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing provider customer id, if they already have saved cards, so
        // all of a shopper's cards belong to one provider customer.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        var customerId = existing.Select(c => c.CustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var idempotencyKey = $"vault-{Guid.NewGuid():N}";
        var result = await _gateway.VaultCardAsync(card, customerId, idempotencyKey, ct);

        var saved = new SavedPaymentMethod(
            buyerId,
            result.VaultId,
            result.CustomerId ?? customerId,
            result.CardBrand,
            result.LastFourDigits,
            result.Expiry ?? string.Empty,
            result.CardholderName);

        saved = await _repository.AddAsync(saved, ct);
        _logger.LogInformation($"Shopper {buyerId} saved card {saved.CardBrand} ****{saved.LastFourDigits} (vault {result.VaultId}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped to the owner: one shopper can never delete another's card.
        var card = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId, buyerId), ct);
        if (card is null)
        {
            return false;
        }

        // Remove from the provider first so the card can no longer be charged, then from our store.
        await _gateway.DeleteVaultedCardAsync(card.VaultId, ct);
        await _repository.DeleteAsync(card, ct);

        _logger.LogInformation($"Shopper {buyerId} removed saved card {paymentMethodId} (vault {card.VaultId}).");
        return true;
    }
}
