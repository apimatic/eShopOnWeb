using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

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

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var idempotencyKey = Guid.NewGuid().ToString();
        var vault = await _payPal.VaultCardAsync(card, idempotencyKey, ct);

        var savedCard = new SavedCard(buyerId, vault.VaultId, vault.Brand, vault.Last4, vault.Expiry, vault.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, ct);
        _logger.LogInformation("Saved card {0} ({1} ****{2}) vaulted for {3}.", savedCard.Id, vault.Brand, vault.Last4, buyerId);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
        return cards;
    }

    public async Task DeleteAsync(string buyerId, int savedCardId, CancellationToken ct = default)
    {
        // Scoped to the owner: one shopper can never delete another's card (reported as not found).
        var card = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdForBuyerSpecification(savedCardId, buyerId), ct)
            ?? throw new NotFoundException($"Saved card {savedCardId} was not found.");

        await _payPal.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        await _savedCardRepository.DeleteAsync(card, ct);
        _logger.LogInformation("Saved card {0} deleted for {1}; vault token removed.", savedCardId, buyerId);
    }
}
