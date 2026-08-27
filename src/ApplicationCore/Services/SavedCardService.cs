using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPalGateway;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalGateway payPalGateway)
    {
        _savedCardRepository = savedCardRepository;
        _payPalGateway = payPalGateway;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        var vaulted = await _payPalGateway.SaveCardAsync(buyerId, card,
            Guid.NewGuid().ToString(), cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.Brand,
            vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName ?? card.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (savedCard == null || savedCard.BuyerId != buyerId)
        {
            throw new SavedCardNotFoundException(paymentMethodId);
        }

        await _payPalGateway.DeleteSavedCardAsync(savedCard.VaultTokenId, cancellationToken);
        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
    }
}
