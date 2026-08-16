using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves and manages a shopper's vaulted cards. The card itself is vaulted with PayPal; this service
/// stores only the returned vault token and a safe description, always scoped to the owning shopper.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private const string Provider = "PayPal";

    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse a PayPal customer id already established for this shopper so their tokens are grouped.
        var existing = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        string? customerId = null;
        foreach (var e in existing)
        {
            if (!string.IsNullOrEmpty(e.PayPalCustomerId))
            {
                customerId = e.PayPalCustomerId;
                break;
            }
        }

        var idempotencyKey = $"vault-{buyerId}-{Guid.NewGuid():N}";
        var vaulted = await _paymentGateway.VaultCardAsync(card, customerId, idempotencyKey, cancellationToken);

        var savedCard = new SavedCard(buyerId, Provider, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.Last4, vaulted.ExpiryYearMonth, vaulted.CardholderName);
        savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved card {savedCard.Id} ({savedCard.DisplayLabel}) for {buyerId}; vault token stored, no card data retained.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return (IReadOnlyList<SavedCard>)cards;
    }

    public async Task<SavedCard?> GetOwnedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdForBuyerSpecification(buyerId, paymentMethodId), cancellationToken);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdForBuyerSpecification(buyerId, paymentMethodId), cancellationToken);
        if (card is null)
        {
            return false;
        }

        // Remove the token from PayPal's vault so it can no longer be charged; then drop our record.
        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(card.PayPalVaultId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Deleting PayPal vault token for saved card {paymentMethodId} failed ({ex.Message}); removing local record so it is no longer usable.");
        }

        await _savedCardRepository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation($"Saved card {paymentMethodId} removed for {buyerId}.");
        return true;
    }
}
