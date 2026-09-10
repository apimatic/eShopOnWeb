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
/// Saves, lists and removes a shopper's cards. Cards are vaulted at PayPal; the application stores only
/// PayPal's vault token and a safe description (brand, last four, expiry) — never the full card number.
/// Every operation is scoped to the calling shopper.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalClient payPalClient,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // A stable, merchant-scoped PayPal customer id per shopper, so the shopper's vaulted cards are
        // filed together on PayPal's side too.
        var customerId = PayPalCustomerIdFor(buyerId);
        var idempotencyKey = $"vault-{customerId}-{Guid.NewGuid():N}";

        var vaulted = await _payPalClient.VaultCardAsync(card, customerId, idempotencyKey, cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultId, vaulted.CustomerId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved card {savedCard.Id} ({vaulted.Brand} ****{vaulted.Last4}) for {buyerId}.");
        return ToView(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards.Select(ToView).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var card = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpecification(paymentMethodId), cancellationToken);

        // Not-owned is reported as not-found so a shopper cannot probe for or delete another's cards.
        if (card is null || card.BuyerId != buyerId)
        {
            throw new SavedCardNotFoundException(paymentMethodId);
        }

        // Remove from PayPal's vault so it can no longer be used to pay, then from our own store.
        await _payPalClient.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for {buyerId}.");
    }

    private static string PayPalCustomerIdFor(string buyerId)
    {
        // A deterministic, opaque, PayPal-safe id derived from the buyer id.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(buyerId)));
        return $"eshop-{hash.Substring(0, 16).ToLowerInvariant()}";
    }

    private static SavedCardView ToView(SavedCard c) => new(c.Id, c.Brand, c.Last4, c.Expiry, c.CardholderName);
}
