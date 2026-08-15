using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPayPalPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, string? label, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Vault the card with PayPal. Full card details are never persisted in the app's own store.
        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId: buyerId,
            vaultId: vaulted.VaultId,
            cardBrand: vaulted.Brand ?? string.Empty,
            lastFourDigits: vaulted.LastFourDigits,
            expiryMonth: vaulted.ExpiryMonth,
            expiryYear: vaulted.ExpiryYear,
            label: label);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task RemoveAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var card = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(savedPaymentMethodId, buyerId), cancellationToken)
            ?? throw new SavedPaymentMethodNotFoundException(savedPaymentMethodId);

        // Remove from PayPal's vault first so it can no longer be used to pay, then from the app store.
        await _gateway.RemoveVaultedCardAsync(card.VaultId, cancellationToken);
        await _repository.DeleteAsync(card, cancellationToken);
    }
}
