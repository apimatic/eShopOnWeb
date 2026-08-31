using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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

    public async Task<SavedCard> SaveAsync(string buyerId, GatewayCardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _paymentGateway.VaultCardAsync(
            card,
            merchantCustomerId: buyerId,
            idempotencyKey: $"vault-{buyerId}-{Guid.NewGuid():N}",
            ct);

        var savedCard = new SavedCard(buyerId, vaulted.PayPalCustomerId, vaulted.PaymentTokenId,
            vaulted.LastDigits, vaulted.Brand, vaulted.Expiry);
        await _savedCardRepository.AddAsync(savedCard, ct);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int savedCardId, CancellationToken ct = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new EntityNotFoundException($"Saved card {savedCardId} was not found.");
        }

        await _paymentGateway.DeletePaymentTokenAsync(savedCard.PaymentTokenId, ct);
        await _savedCardRepository.DeleteAsync(savedCard, ct);
    }
}
