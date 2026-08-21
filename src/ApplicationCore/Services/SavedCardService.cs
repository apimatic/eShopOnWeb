using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's vaulted cards. The card number is only ever sent to PayPal;
/// the application stores nothing but PayPal's vault token id and a safe descriptor.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPayPalPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(PayPalCardDetails card, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(card, nameof(card));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var vaulted = await _gateway.VaultCardAsync(card, buyerId, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand, vaulted.LastFourDigits, vaulted.Expiry);
        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteCardAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            return false; // not found or not the caller's — either way, nothing to remove
        }

        // Remove from PayPal's vault first so a deleted card can no longer be used to pay.
        await _gateway.DeleteVaultedCardAsync(saved.VaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
        return true;
    }
}
