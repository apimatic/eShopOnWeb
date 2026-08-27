using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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

    public async Task<SavedCard> SaveCardAsync(string buyerId, GatewayCardDetails card)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _paymentGateway.SaveCardAsync(buyerId, card, $"eshop-vault-{Guid.NewGuid():N}");

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(savedCard);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId));
    }

    public async Task DeleteSavedCardAsync(string buyerId, int savedCardId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId));
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            // Do not leak the existence of another shopper's saved card.
            throw new NotFoundException(savedCardId.ToString(), nameof(SavedCard));
        }

        try
        {
            await _paymentGateway.DeleteSavedCardAsync(savedCard.VaultTokenId);
        }
        catch (PaymentGatewayException)
        {
            // The token may already be gone at PayPal; the local record is removed regardless
            // so the card can no longer be used to pay.
        }

        await _savedCardRepository.DeleteAsync(savedCard);
    }
}
