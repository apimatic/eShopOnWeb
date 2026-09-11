using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPalClient;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalClient payPalClient)
    {
        _savedCardRepository = savedCardRepository;
        _payPalClient = payPalClient;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, PayPalCard card, CancellationToken cancellationToken = default)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
            throw new InvalidRequestException("Card number, expiry and security code are all required to save a card.");

        var existing = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);

        // Reuse the shopper's PayPal customer id so all their vaulted cards live under one customer.
        var customerId = existing.FirstOrDefault()?.PayPalCustomerId;

        // A fresh key per save request: it must be unique so a token deleted in an earlier run is
        // never re-served from PayPal's idempotency cache.
        var idempotencyKey = $"vault-{Guid.NewGuid():N}";
        var result = await _payPalClient.VaultCardAsync(card, customerId, idempotencyKey, cancellationToken);

        var already = existing.FirstOrDefault(c => c.VaultTokenId == result.VaultTokenId);
        if (already is not null)
            return already;

        var savedCard = new SavedCard(buyerId, result.VaultTokenId, result.CustomerId, result.Brand ?? string.Empty, result.Last4 ?? string.Empty, result.Expiry ?? card.Expiry);
        savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);
        return savedCard;
    }

    // (no card data is ever hashed or stored locally)

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId, cancellationToken);

        // Do not reveal the existence of another shopper's card.
        if (savedCard is null || !string.Equals(savedCard.BuyerId, buyerId, StringComparison.Ordinal))
            throw new EntityNotFoundException($"No saved card found with id {paymentMethodId}.");

        await _payPalClient.DeleteVaultedCardAsync(savedCard.VaultTokenId, cancellationToken);
        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
    }
}
