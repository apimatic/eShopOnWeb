using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalRawCard card,
        CancellationToken cancellationToken)
    {
        // Reuse the shopper's PayPal customer id across saves so their vaulted cards stay grouped.
        var existing = await _repository.ListAsync(new CustomerSavedPaymentMethodsSpecification(buyerId), cancellationToken);
        var customerId = existing
            .Select(c => c.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var vaulted = await _payPal.VaultCardAsync(card, customerId, Guid.NewGuid().ToString("N"), cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId ?? customerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var cards = await _repository.ListAsync(new CustomerSavedPaymentMethodsSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var card = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (card is null || card.BuyerId != buyerId)
        {
            // Not the caller's card (or not found): nothing to remove, and no existence leak.
            return false;
        }

        // Remove from PayPal's vault first so it can no longer be used to pay, then from our store.
        await _payPal.DeleteVaultedCardAsync(card.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(card, cancellationToken);
        return true;
    }
}
