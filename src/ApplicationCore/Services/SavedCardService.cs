using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPayPalClient payPal,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, PayPalCardInput card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card number and expiry are required to save a card.");
        }

        // Group a shopper's vaulted cards under a stable PayPal customer id derived from the
        // buyer id (kept within PayPal's 22-char limit and character set).
        var customerId = StableCustomerId(buyerId);
        var result = await _payPal.VaultCardAsync(card, customerId, Guid.NewGuid().ToString("N"), ct);

        var saved = new SavedCard(buyerId, result.VaultId, result.CardBrand, result.LastFour,
            result.Expiry, result.CardHolderName);
        saved = await _savedCardRepository.AddAsync(saved, ct);

        _logger.LogInformation("Saved {0} card ending {1} for {2} (vault {3}).",
            result.CardBrand, result.LastFour, buyerId, result.VaultId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
        return cards;
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var card = await _savedCardRepository.GetByIdAsync(paymentMethodId, ct);
        // A missing card and another shopper's card are treated identically so ownership cannot
        // be probed, and neither can be deleted.
        if (card is null || card.BuyerId != buyerId)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        await _payPal.DeleteVaultTokenAsync(card.PayPalVaultId, ct);
        await _savedCardRepository.DeleteAsync(card, ct);
        _logger.LogInformation("Deleted saved card {0} (vault {1}) for {2}.",
            paymentMethodId, card.PayPalVaultId, buyerId);
    }

    private static string StableCustomerId(string buyerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return "c" + hex.Substring(0, 20); // 21 chars, matches [0-9a-zA-Z_-]{1,22}
    }
}
