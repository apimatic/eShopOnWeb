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

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's vaulted cards. The card is stored in PayPal's vault; this app
/// keeps only the vault token and safe display detail, always scoped to the owning shopper.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _repository;
    private readonly IPayPalPaymentService _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> repository, IPayPalPaymentService payPal,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardInput card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var payPalCard = new PayPalCard(
            card.Number,
            NormalizeExpiry(card.Expiry),
            card.SecurityCode,
            card.Name,
            card.BillingAddress == null
                ? null
                : new PayPalBillingAddress(card.BillingAddress.Line1, card.BillingAddress.Line2,
                    card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
                    card.BillingAddress.CountryCode));

        var vaulted = await _payPal.VaultCardAsync(payPalCard, Guid.NewGuid().ToString("N"), cancellationToken);

        var saved = new SavedCard(buyerId, vaulted.VaultId, vaulted.CustomerId, vaulted.Brand, vaulted.Last4,
            vaulted.Expiry, vaulted.Name ?? card.Name);
        saved = await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _repository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _repository.FirstOrDefaultAsync(
            new SavedCardByIdForBuyerSpecification(savedCardId, buyerId), cancellationToken);
        if (card == null)
        {
            throw new EntityNotFoundException($"Saved card {savedCardId} was not found.");
        }

        // Remove from PayPal's vault first; if PayPal no longer knows it, we still drop our record so
        // the card can never be used to pay again.
        try
        {
            await _payPal.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning(
                $"PayPal vault delete for token of saved card {savedCardId} failed ({ex.Issue ?? ex.Message}); " +
                "removing the local record anyway.");
        }

        await _repository.DeleteAsync(card, cancellationToken);
    }

    private static string NormalizeExpiry(string expiry)
    {
        var value = (expiry ?? string.Empty).Trim();
        if (value.Length == 7 && value[4] == '-')
        {
            return value;
        }
        if (value.Contains('/'))
        {
            var parts = value.Split('/');
            if (parts.Length == 2)
            {
                var month = parts[0].PadLeft(2, '0');
                var year = parts[1].Length == 2 ? "20" + parts[1] : parts[1];
                return $"{year}-{month}";
            }
        }
        return value;
    }
}
