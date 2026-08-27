using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPaymentGateway paymentGateway)
    {
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _paymentGateway.VaultCardAsync(
            buyerId, card, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits,
            vaulted.Expiry, vaulted.CardholderName);
        return await _savedCardRepository.AddAsync(savedCard, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdAndBuyerSpec(savedCardId, buyerId), cancellationToken);
        if (savedCard is null)
        {
            return false;
        }

        await _paymentGateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, cancellationToken);
        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        return true;
    }
}
