using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Manages a shopper's saved (vaulted) cards; every operation is scoped to the owner.</summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalGateway gateway)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
    }

    public async Task<SavedCard> SaveCardAsync(string ownerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.Null(card, nameof(card));

        // A unique request id per save; a saved card is a distinct vault entry the shopper can remove.
        var idempotencyKey = $"vault-{Guid.NewGuid()}";
        var result = await _gateway.VaultCardAsync(card, idempotencyKey, ct);

        var saved = new SavedCard(ownerId, result.VaultId, result.CardBrand, result.CardLastFour, result.CardExpiry);
        return await _savedCardRepository.AddAsync(saved, ct);
    }

    public async Task<IReadOnlyList<SavedCard>> GetCardsAsync(string ownerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByOwnerSpecification(ownerId), ct);
    }

    public async Task DeleteCardAsync(string ownerId, int savedCardId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdForOwnerSpecification(savedCardId, ownerId), ct);
        if (savedCard is null)
            throw new PaymentException($"Saved card {savedCardId} was not found for this shopper.", PaymentErrorKind.NotFound);

        try
        {
            await _gateway.DeleteVaultedCardAsync(savedCard.PayPalVaultId, ct);
        }
        catch (PaymentException ex) when (ex.Kind == PaymentErrorKind.NotFound)
        {
            // Already gone at PayPal — removing the local record still satisfies the delete.
        }

        await _savedCardRepository.DeleteAsync(savedCard, ct);
    }
}
