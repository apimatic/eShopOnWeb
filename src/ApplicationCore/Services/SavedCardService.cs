using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalGateway gateway)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
    }

    public async Task<SavedCardResult> SaveCardAsync(string buyerId, CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card?.Number))
            throw new PaymentStateException("Card details are required to save a payment method.");

        var requestId = Guid.NewGuid().ToString("N");
        var merchantCustomerId = $"eshop-{buyerId}";
        var setup = await _gateway.CreateSetupTokenAsync(card, merchantCustomerId, requestId);

        if (string.Equals(setup.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PayerActionRequiredException(
                "PayPal requires a browser approval (3-D Secure challenge) to save this card. This integration " +
                "is drivable without a browser, so the card was not saved.");

        var token = await _gateway.CreatePaymentTokenAsync(setup.SetupTokenId, Guid.NewGuid().ToString("N"));

        var saved = new SavedCard(buyerId, token.CustomerId, token.PaymentTokenId,
            token.Last4, token.Brand, token.Expiry, token.Name);

        await _savedCardRepository.AddAsync(saved);

        return new SavedCardResult(saved.Id, saved.Last4, saved.Brand, saved.Expiry, saved.CardholderName);
    }

    public async Task<IReadOnlyList<SavedCardDto>> ListCardsAsync(string buyerId)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId));
        return cards
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new SavedCardDto(c.Id, c.Last4, c.Brand, c.Expiry, c.CardholderName,
                $"{c.Brand} ending {c.Last4}", c.CreatedAt.ToString("O")))
            .ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int savedCardId)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId));
        if (saved is null) throw new NotFoundException($"Saved card {savedCardId} not found.");
        if (!string.Equals(saved.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Saved card belongs to another shopper.");

        await _gateway.DeletePaymentTokenAsync(saved.PayPalTokenId);
        await _savedCardRepository.DeleteAsync(saved);
    }
}