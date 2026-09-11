using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Default <see cref="ISavedPaymentMethodService"/> backed by the PayPal Vault.</summary>
public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPal,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentValidationException("Card number and expiry are required to save a card.");
        }

        // Reuse the buyer's existing PayPal customer id so all their saved cards live under one customer.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var customerId = existing.Select(m => m.CustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var vaulted = await _payPal.VaultCardAsync(card, customerId, requestId: Guid.NewGuid().ToString("N"), cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId, vaulted.PaymentTokenId, vaulted.CustomerId ?? customerId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.CardholderName ?? card.CardholderName);

        saved = await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Saved card {saved.Id} ({saved.Brand} ****{saved.Last4}) for {buyerId}.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return items;
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            // Not found, or belongs to another shopper — do not leak which.
            throw new PaymentResourceNotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        // Remove from PayPal's Vault first so the token can no longer be charged, then drop our record.
        await _payPal.DeleteVaultedCardAsync(saved.PaymentTokenId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for {buyerId}.");
    }
}
