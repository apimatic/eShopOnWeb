using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string ownerId, CardDetails card, string? label, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.Null(card, nameof(card));

        // Group all of a shopper's cards under one PayPal customer id, if they already have one.
        var existing = await _savedCardRepository.ListAsync(new SavedCardsByOwnerSpec(ownerId), cancellationToken);
        var existingCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var requestId = Guid.NewGuid().ToString("N");
        var vaulted = await _payPal.VaultCardAsync(card, existingCustomerId, requestId, cancellationToken);

        var savedCard = new SavedCard(ownerId, vaulted.VaultId, vaulted.CustomerId, vaulted.Brand, vaulted.Last4,
            vaulted.Expiry, vaulted.CardholderName, label);
        savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved card for {ownerId}: {savedCard.DisplayName()} (vault {vaulted.VaultId}).");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetCardsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByOwnerSpec(ownerId), cancellationToken);
    }

    public async Task DeleteCardAsync(string ownerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdForOwnerSpec(savedCardId, ownerId), cancellationToken);
        if (savedCard is null)
        {
            throw new PaymentResourceNotFoundException($"Saved payment method {savedCardId} was not found.");
        }

        // Remove from PayPal's vault first so the token can no longer be used to pay, then drop our record.
        await _payPal.DeleteVaultedCardAsync(savedCard.VaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Deleted saved card {savedCardId} for {ownerId} (vault {savedCard.VaultId}).");
    }
}
