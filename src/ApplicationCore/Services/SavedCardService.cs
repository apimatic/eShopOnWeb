using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly ICardVault _vault;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, ICardVault vault,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _vault = vault;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _vault.VaultCardAsync(card, Guid.NewGuid().ToString("N"), cancellationToken);

        var method = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4,
            vaulted.Expiry, card.CardholderName, vaulted.CardType);
        method = await _repository.AddAsync(method, cancellationToken);

        _logger.LogInformation($"Saved a {vaulted.Brand} card ending {vaulted.Last4} for {buyerId} (vault token stored, no card data).");
        return ToView(method);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return cards.OrderByDescending(c => c.CreatedAt).Select(ToView).ToList();
    }

    public async Task RemoveCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (method is null || method.BuyerId != buyerId)
        {
            // Do not reveal another shopper's card.
            throw new PaymentNotFoundException($"Saved card {paymentMethodId} was not found for the caller.");
        }

        // Remove from PayPal's vault first so the card can no longer be used to pay, then drop our record.
        await _vault.DeleteVaultedCardAsync(method.VaultId, cancellationToken);
        await _repository.DeleteAsync(method, cancellationToken);

        _logger.LogInformation($"Removed saved card {paymentMethodId} for {buyerId}.");
    }

    private static SavedCardView ToView(SavedPaymentMethod m) =>
        new(m.Id, m.Brand, m.Last4, m.Expiry, m.CardholderName, m.CardType, m.CreatedAt);
}
