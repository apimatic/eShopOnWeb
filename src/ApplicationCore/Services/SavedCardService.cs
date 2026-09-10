using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's cards via PayPal's vault. Full card details never touch the
/// app's database — only the PayPal vault token and a safe descriptor are kept. Ownership is scoped
/// to the shopper who saved the card.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, SaveCardInput input, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(input.CardNumber) || string.IsNullOrWhiteSpace(input.Expiry))
        {
            throw new PaymentException("Card number and expiry are required to save a card.", 400);
        }

        // Reuse the PayPal customer id already assigned to this shopper, so all their cards group together.
        var existingCards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
        var existingCustomerId = existingCards
            .Select(c => c.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var card = new CardPaymentInput
        {
            Number = input.CardNumber,
            Expiry = input.Expiry,
            SecurityCode = input.SecurityCode,
            CardholderName = input.CardholderName
        };

        var result = await _gateway.VaultCardAsync(
            card, buyerReference: buyerId, existingCustomerId, idempotencyKey: $"vault-{Guid.NewGuid():N}", ct);

        var saved = new SavedCard(
            buyerId,
            result.VaultTokenId,
            result.CustomerId ?? existingCustomerId,
            result.Brand,
            result.LastDigits,
            result.Expiry,
            result.CardholderName ?? input.CardholderName);

        await _savedCardRepository.AddAsync(saved, ct);
        return saved;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int savedCardId, CancellationToken ct)
    {
        var card = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (card is null || !string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return false; // not the caller's card (or gone) — do not reveal another shopper's data
        }

        // Remove the vault token at PayPal, then locally. If PayPal's vault delete is unavailable,
        // still remove locally so the app-level guarantee holds — the card no longer appears for the
        // caller and can no longer be used to pay (paying resolves the card from this store) — and
        // record the dangling token for follow-up.
        try
        {
            await _gateway.DeleteVaultedCardAsync(card.VaultTokenId, ct);
        }
        catch (PayPalGatewayException ex)
        {
            _logger.LogWarning(
                "PayPal vault delete failed for token {0} (card {1}); removing locally anyway. {2}",
                card.VaultTokenId, card.Id, ex.Message);
        }

        await _savedCardRepository.DeleteAsync(card, ct);
        return true;
    }
}
