using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
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

    public async Task<SavedCard> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var customerId = PayPalCustomerId.FromBuyerId(buyerId);

        var setupTokenId = await _payPal.CreateSetupTokenAsync(customerId, card,
            $"eshop-setup-{customerId}-{Guid.NewGuid():N}", cancellationToken);

        var paymentTokenId = await _payPal.CreatePaymentTokenAsync(customerId, setupTokenId,
            $"eshop-vault-{customerId}-{Guid.NewGuid():N}", cancellationToken);

        // Fetch the durable token's safe display data (brand, last digits, expiry).
        var vaulted = await _payPal.GetPaymentTokenAsync(paymentTokenId, cancellationToken);

        var savedCard = new SavedCard(buyerId, customerId, paymentTokenId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved card ending in {savedCard.LastDigits} for shopper as payment method {savedCard.Id}.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId), cancellationToken);
        if (savedCard is null || !string.Equals(savedCard.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Existence of other shoppers' cards is never revealed.
            throw new NotFoundException($"Payment method {savedCardId} was not found.");
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(savedCard.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal; still remove it locally so it cannot be used.
            _logger.LogWarning($"PayPal payment token for saved card {savedCardId} was already deleted at PayPal.");
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
    }
}
