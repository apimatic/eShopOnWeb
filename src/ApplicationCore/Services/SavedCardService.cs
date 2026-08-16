using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalClient payPal,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Group all of a shopper's cards under one PayPal customer id.
        var existing = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
        var existingCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var vaulted = await _payPal.VaultCardAsync(card, existingCustomerId, Guid.NewGuid().ToString(), ct);

        var saved = new SavedCard(buyerId, vaulted.CustomerId, vaulted.PaymentTokenId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.Name);
        saved = await _savedCardRepository.AddAsync(saved, ct);

        _logger.LogInformation($"Saved card {saved.Id} ({vaulted.Brand} ****{vaulted.Last4}) for buyer {buyerId}.");
        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
        return cards.OrderByDescending(c => c.CreatedAt).Select(ToView).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped by buyer: one shopper can never delete another's card.
        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByBuyerAndIdSpecification(buyerId, paymentMethodId), ct);
        if (card is null)
        {
            return false;
        }

        // Remove from PayPal's vault first, so the card can no longer be used to pay,
        // then drop the local record.
        await _payPal.DeleteVaultedCardAsync(card.PaymentTokenId, ct);
        await _savedCardRepository.DeleteAsync(card, ct);

        _logger.LogInformation($"Deleted saved card {paymentMethodId} for buyer {buyerId}.");
        return true;
    }

    private static SavedCardView ToView(SavedCard c) =>
        new(c.Id, c.Brand, c.Last4, c.Expiry, c.CardholderName, c.CreatedAt);
}
