using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PaymentCard card,
        CancellationToken cancellationToken = default)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new InvalidPaymentRequestException("Card number and expiry are required to save a card.");
        }

        // Keep all of a shopper's cards under one PayPal customer id.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var customerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var result = await _gateway.VaultCardAsync(new VaultCardRequest(card, customerId), cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, result.TokenId, result.CustomerId,
            result.Brand, result.LastFourDigits, result.Expiry, result.CardholderName);
        saved = await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default) =>
        await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);

    public async Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        // A shopper must never delete another's card; hide existence either way.
        if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            // Removing the vault token is best-effort; the local record is what makes the card
            // usable in this app, so we still remove it to honor the delete.
            _logger.LogWarning($"Could not delete PayPal vault token for saved card {paymentMethodId}: {ex.Message}");
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
